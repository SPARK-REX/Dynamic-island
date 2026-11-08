// ====================================================================================================
// Author: Akhin (SPARK-REX) & Antigravity
// Project: Dynamic Island for Windows - Gemini Multimodal Voice Assistant Service
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DynamicIsland.Services;

/// <summary>
/// Gemini-backed multimodal voice assistant service.
/// - Push-to-Talk via Control key (Left or Right Ctrl).
/// - Clean Wake-word detection ("Hey Nixi" / "Nixi") with zero offline command interference.
/// - WasapiCapture with automatic Voice Activity & Silence Detection (VAD).
/// - Hardware-independent audio downmix & resampling to standard 16 kHz Mono 16-bit PCM WAV.
/// - Direct multimodal tool-calling via Gemini 2.5 Flash (apps, media, volume, web search, system info, chat).
/// - Clean on-screen island notification feedback (silent mode).
/// </summary>
public sealed class VoiceAssistantService : IDisposable
{
    // ---- Win32 Low-Level Keyboard Hook ----
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    // Media Keys
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_SNAPSHOT = 0x2C;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _modelThrottledUntil = new();
    private static readonly string[] CandidateModels = ["gemini-2.5-flash-lite", "gemini-2.5-flash", "gemini-3.1-flash-lite-preview", "gemini-3.1-flash-lite", "gemini-3.5-flash"];
    private const string GeminiApiBase = "https://generativelanguage.googleapis.com/v1beta";
    private const string NixiSystemPrompt =
        "You are Nixi, a friendly female voice assistant for Windows. The audio may be quiet or noisy: listen carefully and transcribe exactly what the user said. " +
        "Open ANY application the user names with open_application (use the app name exactly as spoken; never refuse or guess a different app). " +
        "Close/quit/exit a running app with close_application. " +
        "For media, volume, screenshot, brightness, web search or time/date/battery use the matching tool. " +
        "For chat or questions use conversation_reply with a 1-2 sentence spoken answer. Always set 'transcript'.";
    private const string NixiLocalSystemPrompt =
        "You are Nixi, a Windows desktop voice assistant. You receive the transcript of one short spoken command. " +
        "Call exactly one tool that best matches the request. open_application launches ANY installed app (use the app name exactly as spoken); " +
        "close_application closes/quits a running app. For media, volume, screenshot, brightness, web search, time/date/battery use the matching tool. " +
        "For greetings, questions or chit-chat use conversation_reply with a short 1-2 sentence spoken answer. " +
        "Always include 'transcript' with the user's exact words.";
    private const int MaximumAudioBytes = 12 * 1024 * 1024;
    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _hookId = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ---- Speech Components ----
    private SpeechRecognitionEngine? _speechEngine;
    private static readonly SocketsHttpHandler _httpHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        EnableMultipleHttp2Connections = true
    };
    private static readonly HttpClient _httpClient = new(_httpHandler) { Timeout = TimeSpan.FromSeconds(25) };
    private readonly object _captureLock = new();
    private WasapiCapture? _audioCapture;
    private MemoryStream? _audioBuffer;
    private TaskCompletionSource<bool>? _captureStopped;
    private int _pushToTalkVirtualKey = VK_CONTROL;
    private CancellationTokenSource? _ttsCts;
    private System.Media.SoundPlayer? _activeSoundPlayer;

    private volatile bool _isListening;
    private readonly object _listeningStateLock = new();
    private bool _speechRecognitionActive;
    private TaskCompletionSource<bool>? _localRecognitionStopped;
    private int _currentTurnId;
    private bool _ctrlDown;
    private bool _ctrlHadOtherKeys;
    private bool _disposed;
    private CancellationTokenSource? _listeningTimeoutCts;
    private DateTime _lastSpeechUtc = DateTime.UtcNow;
    private bool _speechDetectedInSession;
    private volatile bool _turnIsPushToTalk;
    private volatile bool _turnIsWakeWord;
    private volatile bool _uiShown;
    private float _noiseFloor = 1f;
    private volatile bool _lastErrorWasQuotaExceeded;

    // ---- Local model mode (Ollama on this PC) ----
    private string _modelProvider = "cloud";   // "cloud" (Gemini) | "local" (Ollama)
    private string _localModel = "qwen2.5:7b";
    private string _localEndpoint = "http://localhost:11434/v1";
    private readonly StringBuilder _dictation = new();
    private Grammar? _dictationGrammar;
    private volatile bool _transcribing;
    public bool IsLocalModel => _modelProvider == "local";
    public void SetModelProvider(string provider)
    {
        _modelProvider = provider == "local" ? "local" : "cloud";
        Program.Log($"Model provider set to: {_modelProvider}");
    }    public void SetLocalModel(string model) { if (!string.IsNullOrWhiteSpace(model)) _localModel = model.Trim(); }
    public void SetLocalEndpoint(string endpoint) { if (!string.IsNullOrWhiteSpace(endpoint)) _localEndpoint = endpoint.Trim(); }

    // ---- Optional Injected Actions ----
    public Action<float>? SetVolumeAction { get; set; }
    /// <summary>Skip via island media session. Arg: true=next, false=previous. Returns false if no session.</summary>
    public Func<bool, bool>? SkipTrackAction { get; set; }
    public Func<float>? GetVolumeFunc { get; set; }

    // ---- Events ----
    public event Action<bool, string>? ListeningStateChanged;
    public event Action<string>? CommandRecognized;
    public event Action<string, string>? ActionExecuted; // (ActionDescription, ReplyVoiceText)
    public event Action<float>? AudioLevelChanged; // Live mic volume level (0.0 - 1.0)

    public bool IsListening => _isListening;

    private volatile bool _pushToTalkEnabled = true;
    public bool PushToTalkEnabled => _pushToTalkEnabled;
    public void SetPushToTalkEnabled(bool enabled) => _pushToTalkEnabled = enabled;

    private int _pushToTalkModifierKey = 0;
    public int PushToTalkModifierKey => _pushToTalkModifierKey;
    private string _pushToTalkShortcutName = "";

    public string PushToTalkShortcut => !string.IsNullOrEmpty(_pushToTalkShortcutName) && _pushToTalkModifierKey != 0
        ? _pushToTalkShortcutName
        : _pushToTalkVirtualKey switch
        {
            0 => "Disabled",
            VK_CONTROL => "Ctrl (Any)",
            VK_LCONTROL => "Left Ctrl",
            VK_RCONTROL => "Right Ctrl",
            VK_MENU => "Alt (Any)",
            VK_LMENU => "Left Alt",
            VK_RMENU => "Right Alt",
            0x14 => "Caps Lock",
            0x20 => "Space",
            >= 0x70 and <= 0x7B => $"F{_pushToTalkVirtualKey - 0x70 + 1}",
            _ => ((System.Windows.Forms.Keys)_pushToTalkVirtualKey).ToString()
        };

    public void SetPushToTalkVirtualKey(int virtualKey)
    {
        if (virtualKey is >= 0 and < 256) _pushToTalkVirtualKey = virtualKey;
        _pushToTalkModifierKey = 0;
        _pushToTalkShortcutName = "";
    }

    public void SetPushToTalkShortcut(string name, int virtualKey, int modifierKey)
    {
        _pushToTalkShortcutName = name;
        if (virtualKey is >= 0 and < 256) _pushToTalkVirtualKey = virtualKey;
        _pushToTalkModifierKey = modifierKey;
    }

    private readonly object _wakeReloadLock = new();
    private string _wakePhrase = "Hey Nixi";
    public string WakePhrase => _wakePhrase;
    public void SetWakePhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;
        _wakePhrase = phrase.Trim();
        var engine = _speechEngine;
        if (engine == null) return; // grammar is built from _wakePhrase when engine starts
        _ = Task.Run(() =>
        {
            lock (_wakeReloadLock)
            {
                try
                {
                    if (_wakeWordEnabled) engine.RecognizeAsyncCancel();
                    foreach (var g in engine.Grammars.ToArray())
                        if (g.Name is "WakeWord" or "WakeGarbage") engine.UnloadGrammar(g);
                    { engine.LoadGrammar(BuildWakeGrammar()); engine.LoadGrammar(BuildGarbageGrammar()); }
                    if (_wakeWordEnabled) engine.RecognizeAsync(RecognizeMode.Multiple);
                }
                catch (Exception ex) { Program.Log($"SetWakePhrase reload failed: {ex.Message}"); }
            }
        });
    }

    // SAPI has no "nixi" in its dictionary: include real-word look-alikes it can actually hear.
    private Grammar BuildWakeGrammar()
    {
        string[] nixi = { "nixi", "nixy", "nixie", "niksi", "nicky", "nicki", "nexi" };
        string[] words = _wakePhrase.ToLowerInvariant() switch
        {
            // single-word phrases are hard for SAPI: also accept the "hey ..." forms
            "nixi" => nixi.Concat(nixi.Select(n => "hey " + n)).ToArray(),
            "island" => new[] { "island", "hey island" },
            _ => nixi.Select(n => "hey " + n).ToArray()
        };
        return new Grammar(new GrammarBuilder(new Choices(words))) { Name = "WakeWord" };
    }

    // Rejection grammar: SAPI force-fits ANY speech to the nearest wake word when it is the only grammar.
    // Dictation competes: normal speech matches it (ignored), only a real "hey nixi" beats it.
    private static Grammar BuildGarbageGrammar() => new DictationGrammar { Name = "WakeGarbage" };

    private string _microphoneDeviceId = "";
    public string MicrophoneDeviceId => _microphoneDeviceId;
    public void SetMicrophoneDevice(string deviceId) => _microphoneDeviceId = deviceId ?? "";

    private int _microphoneSensitivity = 72;
    public int MicrophoneSensitivity => _microphoneSensitivity;
    public void SetMicrophoneSensitivity(int sensitivity) => _microphoneSensitivity = Math.Clamp(sensitivity, 0, 100);

    private WasapiCapture? _meterCapture;
    private int _meterSubscribers = 0;

    public void StartAudioMeter()
    {
        lock (_captureLock)
        {
            _meterSubscribers++;
            if (_meterSubscribers == 1 && _meterCapture == null && _audioCapture == null)
            {
                try
                {
                    _meterCapture = CreateCaptureDevice();
                    _meterCapture.DataAvailable += (_, e) =>
                    {
                        if (e.BytesRecorded > 0 && _meterCapture != null)
                        {
                            float rms = CalculateRms(e.Buffer, e.BytesRecorded, _meterCapture.WaveFormat);
                            AudioLevelChanged?.Invoke(rms);
                        }
                    };
                    _meterCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    Program.Log($"StartAudioMeter error: {ex.Message}");
                    _meterCapture = null;
                }
            }
        }
    }

    public void StopAudioMeter()
    {
        lock (_captureLock)
        {
            _meterSubscribers = Math.Max(0, _meterSubscribers - 1);
            if (_meterSubscribers == 0 && _meterCapture != null)
            {
                try
                {
                    _meterCapture.StopRecording();
                    _meterCapture.Dispose();
                }
                catch { }
                _meterCapture = null;
            }
        }
    }

    public static System.Collections.Generic.List<(string Id, string Name)> GetAudioInputDevices()
    {
        var list = new System.Collections.Generic.List<(string Id, string Name)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add((dev.ID, dev.FriendlyName));
            }
        }
        catch (Exception ex)
        {
            Program.Log($"GetAudioInputDevices error: {ex.Message}");
        }
        if (list.Count == 0)
        {
            list.Add(("", "Default - Windows Audio Device"));
        }
        return list;
    }

    private WasapiCapture CreateCaptureDevice()
    {
        if (!string.IsNullOrEmpty(_microphoneDeviceId))
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    if (dev.ID == _microphoneDeviceId || dev.FriendlyName.Equals(_microphoneDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return new WasapiCapture(dev);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log($"CreateCaptureDevice specific device error: {ex.Message}");
            }
        }
        return new WasapiCapture();
    }

    private volatile bool _wakeWordEnabled = true;
    public bool WakeWordEnabled => _wakeWordEnabled;

    public void SetWakeWordEnabled(bool enabled)
    {
        if (!enabled && _isListening && _turnIsWakeWord) CancelWakeWordSession();
        _wakeWordEnabled = enabled;

        try
        {
            if (_speechEngine == null) return;
            lock (_wakeReloadLock)
            {
                if (_transcribing)
                {
                    Program.Log($"Wake-word {(enabled ? "enabled" : "disabled")}; current push-to-talk transcription keeps mic until release.");
                    return;
                }
                if (enabled && !_speechRecognitionActive)
                {
                    _speechEngine.SetInputToDefaultAudioDevice();
                    _speechEngine.RecognizeAsync(RecognizeMode.Multiple);
                    _speechRecognitionActive = true;
                    Program.Log("Wake-word engine started.");
                }
                else if (!enabled && _speechRecognitionActive)
                {
                    _speechEngine.RecognizeAsyncCancel();
                    Program.Log("Wake-word engine stopping; microphone releases when recognition stops.");
                }
                else if (!enabled)
                {
                    _speechEngine.SetInputToNull();
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log($"SetWakeWordEnabled error: {ex.Message}");
        }
    }
    private static readonly object[] ToolDeclarations =
    [
                new
                {
                    name = "open_application",
                    description = "Opens or launches ANY application, game, or tool installed on this Windows PC (e.g. Spotify, Chrome, Word, Excel, VS Code, Notepad, Discord, Steam, Photoshop, Settings). Use the name exactly as the user said it.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            app_name = new { type = "STRING", description = "The application name exactly as the user said it, e.g. 'spotify', 'word', 'photoshop'." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "app_name", "transcript" }
                    }
                },
                new
                {
                    name = "close_application",
                    description = "Closes / quits / exits a running application on this Windows PC (e.g. 'close chrome', 'quit spotify', 'exit notepad').",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            app_name = new { type = "STRING", description = "The application name exactly as the user said it." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "app_name", "transcript" }
                    }
                },
                new
                {
                    name = "control_media",
                    description = "Controls media playback or volume. Use 'next' for: next song/track, skip, skip this. Use 'previous' for: previous/last/go back a song/track. Allowed actions: 'play', 'pause', 'play_pause', 'next', 'previous', 'mute', 'unmute', 'volume_up', 'volume_down'.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            action = new { type = "STRING", description = "Action: 'play', 'pause', 'play_pause', 'next', 'previous', 'mute', 'unmute', 'volume_up', 'volume_down'" },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "action", "transcript" }
                    }
                },
                new
                {
                    name = "set_volume",
                    description = "Sets the master audio volume level as a percentage from 0 to 100.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            percent = new { type = "INTEGER", description = "The volume percentage from 0 to 100." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "percent", "transcript" }
                    }
                },
                new
                {
                    name = "search_web",
                    description = "Searches Google in the default web browser for a search query.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            query = new { type = "STRING", description = "The search query to look up on Google." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "query", "transcript" }
                    }
                },
                new
                {
                    name = "take_screenshot",
                    description = "Takes a screenshot of the computer screen, saves it to Pictures\\Screenshots, and copies it to the clipboard.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "transcript" }
                    }
                },
                new
                {
                    name = "adjust_brightness",
                    description = "Adjusts or sets the display screen brightness percentage (e.g. increase, decrease, dim, brighter, or set to specific percentage from 0 to 100).",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            percent = new { type = "INTEGER", description = "Target brightness percentage (0 to 100), or -1 if using relative direction." },
                            direction = new { type = "STRING", description = "Allowed values: 'set', 'increase', 'decrease', 'up', 'down', 'dim', 'brighter'." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "direction", "transcript" }
                    }
                },
                new
                {
                    name = "get_system_info",
                    description = "Retrieves Windows system information such as current time, date, or battery status.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            info_type = new { type = "STRING", description = "Either 'time', 'date', or 'battery'." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "info_type", "transcript" }
                    }
                },
                new
                {
                    name = "conversation_reply",
                    description = "Replies to general conversation, factual questions, greetings, jokes, or chit-chat in 1-2 natural spoken sentences.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            reply = new { type = "STRING", description = "The concise spoken reply (1-2 sentences maximum)." },
                            transcript = new { type = "STRING", description = "The exact words spoken by the user." }
                        },
                        required = new[] { "reply", "transcript" }
                    }
                }
    ];

    private static readonly object[] GeminiTools = [ new { function_declarations = ToolDeclarations } ];

    /// <summary>Same tools in OpenAI chat-completions format, served by Ollama at /v1/chat/completions.</summary>
    private static readonly object[] LocalTools = ToolDeclarations.Select(d => new { type = "function", function = d }).ToArray();

    public VoiceAssistantService()
    {
    }

    /// <summary>
    /// Initializes global keyboard hook and lightweight wake-word listener.
    /// </summary>
    public void Initialize()
    {
        if (_disposed) return;

        // 1. Install Global Low-Level Keyboard Hook for Ctrl push-to-talk
        try
        {
            _keyboardProc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule?.ModuleName), 0);
            Program.Log($"VoiceAssistantService keyboard hook installed. Hook: {_hookId != IntPtr.Zero}");
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to install keyboard hook for VoiceAssistant: {ex.Message}");
        }

        // 2. Setup Wake-Word Only Speech Engine (Zero offline command interference)
        Task.Run(InitSpeechEngine);

        AppFinder.Warmup();

        // 3. Pre-warm HTTPS connection to Google Gemini API to eliminate cold-start TLS handshake latency
        PrewarmGeminiConnection();
    }

    private static void PrewarmGeminiConnection()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, "https://generativelanguage.googleapis.com");
                await _httpClient.SendAsync(req).ConfigureAwait(false);
            }
            catch { }
        });
    }

    private static void EnsureReliableAudioInputToken()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var defaultInput = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            string defaultDeviceName = defaultInput.FriendlyName;

            using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Speech\AudioInput\TokenEnums\MMAudioIn", false);
            string? selected = null;

            if (baseKey != null)
            {
                foreach (string subKeyName in baseKey.GetSubKeyNames())
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    string? devName = subKey?.GetValue("DeviceName")?.ToString() ?? subKey?.GetValue("")?.ToString();
                    if (string.IsNullOrWhiteSpace(devName)) continue;

                    if (devName.Contains(defaultDeviceName, StringComparison.OrdinalIgnoreCase) ||
                        defaultDeviceName.Contains(devName, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = $@"HKEY_CURRENT_USER\Software\Microsoft\Speech\AudioInput\TokenEnums\MMAudioIn\{subKeyName}";
                        break;
                    }
                }
            }

            using var audioInputKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Speech\AudioInput");
            if (!string.IsNullOrEmpty(selected))
            {
                audioInputKey?.SetValue("DefaultTokenId", selected);
                Program.Log($"Speech input follows Windows default microphone: {defaultDeviceName}");
            }
            else
            {
                audioInputKey?.DeleteValue("DefaultTokenId", false);
            }
        }
        catch (Exception ex)
        {
            Program.Log($"Audio token selection note: {ex.Message}");
        }
    }

    private void InitSpeechEngine()
    {
        try
        {
            EnsureReliableAudioInputToken();

            // Try to find an English recognizer; fall back to default if none available
            RecognizerInfo? selectedRecognizer = null;
            foreach (var r in SpeechRecognitionEngine.InstalledRecognizers())
            {
                if (r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ||
                    r.Culture.TwoLetterISOLanguageName.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                {
                    selectedRecognizer = r;
                    break;
                }
                if (selectedRecognizer == null && r.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    selectedRecognizer = r;
                }
            }

            _speechEngine = selectedRecognizer != null
                ? new SpeechRecognitionEngine(selectedRecognizer)
                : new SpeechRecognitionEngine();

            // 1. Wake-word phrase (follows the Settings > Wake phrase choice)
            var wakeGrammar = BuildWakeGrammar();

            try
            {
                _speechEngine.LoadGrammar(wakeGrammar);
                _speechEngine.LoadGrammar(BuildGarbageGrammar());
                Program.Log($"Wake-word grammar loaded: {wakeGrammar.Name}.");
            }
            catch (Exception grammarEx)
            {
                Program.Log($"Failed to load wake-word grammar: {grammarEx.Message}. Using default grammar.");
            }

            _speechEngine.SpeechRecognized += (s, e) =>
            {
                if (_disposed || e.Result == null) return;
                float conf = e.Result.Confidence;
                string text = e.Result.Text?.Trim() ?? "";
                string grammarName = e.Result.Grammar?.Name ?? "";

                // Local model mode: while listening, the dictation grammar is the only one loaded
                if (_transcribing && _isListening)
                {
                    if (conf >= 0.05f && !string.IsNullOrWhiteSpace(text))
                    {
                        lock (_dictation) { _dictation.Append(text); _dictation.Append(' '); }
                        _speechDetectedInSession = true;
                    }
                    return;
                }

                if (grammarName == "WakeWord")
                {
                    if (conf >= 0.6f && !_isListening && _wakeWordEnabled)
                    {
                        Program.Log($"Wake word detected: \"{text}\" [Conf={conf:P0}]");
                        StartListening("Wake Word (" + text + ")");
                    }
                    return;
                }
            };

            // Never recover rejected acoustic noise as commands
            _speechEngine.SpeechRecognitionRejected += (s, e) =>
            {
                float conf = e.Result?.Confidence ?? 0f;
                Program.Log($"Wake word rejected candidate: \"{e.Result?.Text ?? "(noise)"}\" [Conf={conf:P0}]");
            };

            // Engine stops itself on mic change/error: restart so the wake word never dies silently.
            _speechEngine.RecognizeCompleted += (s, e) =>
            {
                lock (_wakeReloadLock)
                {
                    _speechRecognitionActive = false;
                    if (_disposed) return;
                    if (!_wakeWordEnabled && !_transcribing)
                    {
                        bool released = true;
                        try { _speechEngine?.SetInputToNull(); Program.Log("SAPI microphone input released after push-to-talk."); }
                        catch (Exception ex) { released = false; Program.Log($"SAPI input release failed: {ex.Message}"); }
                        _localRecognitionStopped?.TrySetResult(released);
                        return;
                    }
                    Program.Log($"Wake-word engine stopped ({e.Error?.Message ?? (e.Cancelled ? "cancelled" : "completed")}); restarting.");
                    try
                    {
                        _speechEngine?.RecognizeAsync(RecognizeMode.Multiple);
                        _speechRecognitionActive = true;
                    }
                    catch (Exception ex) { Program.Log($"Wake-word restart failed: {ex.Message}"); }
                }
            };

            if (_wakeWordEnabled)
            {
                // Open the microphone only when wake-word listening is enabled.
                try
                {
                    _speechEngine.SetInputToDefaultAudioDevice();
                }
                catch (Exception)
                {
                    Program.Log("SAPI default audio device failed; fallback to NAudio capture not directly supported for SAPI engine.");
                }

                _speechEngine.RecognizeAsync(RecognizeMode.Multiple);
                _speechRecognitionActive = true;
                Program.Log("Wake-word engine started (RecognizeAsync Multiple).");
            }
            else
            {
                _speechEngine.SetInputToNull();
                Program.Log("Wake-word engine disabled per settings; microphone input released.");
            }

            Program.Log($"VoiceAssistantService wake-word engine ready. Gemini handles multimodal voice recognition & tool calling.");
        }
        catch (Exception ex)
        {
            Program.Log($"SpeechRecognitionEngine initialization error: {ex.Message}");
            Program.Log($"If SpeechRecognition is not available, the wake word feature will not work until SAPI is properly configured.");
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _pushToTalkEnabled && _pushToTalkVirtualKey != 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int msg = (int)wParam;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            bool isMatch = false;

            if (_pushToTalkModifierKey != 0)
            {
                // Combo shortcut: modifier key must be pressed along with the target key
                bool modDown = (GetAsyncKeyState(_pushToTalkModifierKey) & 0x8000) != 0;
                if (modDown && vkCode == _pushToTalkVirtualKey)
                {
                    isMatch = true;
                }
            }
            else
            {
                // Single key shortcut (Caps Lock, Space, Ctrl, Alt, F8-F12, etc.)
                isMatch = vkCode == _pushToTalkVirtualKey ||
                    (_pushToTalkVirtualKey == VK_CONTROL && (vkCode == VK_LCONTROL || vkCode == VK_RCONTROL || vkCode == VK_CONTROL)) ||
                    (_pushToTalkVirtualKey == VK_MENU && (vkCode == VK_LMENU || vkCode == VK_RMENU || vkCode == VK_MENU));
            }

            if (isMatch)
            {
                if (isDown)
                {
                    if (!_ctrlDown)
                    {
                        _ctrlDown = true;
                        _ctrlHadOtherKeys = false;
                        OnCtrlPressed();
                    }
                }
                else if (isUp)
                {
                    if (_ctrlDown)
                    {
                        _ctrlDown = false;
                        OnCtrlReleased();
                    }
                }
            }
            else if (_ctrlDown && _pushToTalkModifierKey == 0)
            {
                // Key combination with push-to-talk -> standard shortcut, not push-to-talk
                _ctrlHadOtherKeys = true;
                if (_isListening)
                {
                    StopListening();
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void OnCtrlPressed()
    {
        Program.Log($"Push-to-talk key pressed ({PushToTalkShortcut}).");
        if (_isListening) return;

        // Never do work inside the low-level hook: Windows drops slow hooks silently.
        // Mic starts NOW (no lost words); the "Listening" UI waits 150ms so Ctrl+C / Ctrl+V stay invisible.
        _ = Task.Run(() =>
        {
            if (_isListening) return;
            StartListening($"Push-to-Talk ({PushToTalkShortcut})");
            if (!_ctrlDown || _ctrlHadOtherKeys) StopListening(); // released / chord while mic was starting
        });
    }

    private void OnCtrlReleased()
    {
        Program.Log($"Push-to-talk key released ({PushToTalkShortcut}).");
        if (_isListening)
        {
            if (_ctrlHadOtherKeys)
            {
                StopListening();
                return;
            }

            StopListening();
        }
    }

    public void StartListening(string reason, int uiDelayMs = 0)
    {
        if (!IsLocalModel) { EnsureKeyReady(); PrewarmGeminiConnection(); } // pick a healthy key + overlap TLS reconnect with recording
        if (!IsLocalModel && string.IsNullOrWhiteSpace(ReadGeminiApiKey()))
        {
            SpeakAndNotify("Gemini key missing", "Online voice commands need a Gemini API key.");
            return;
        }
        lock (_listeningStateLock)
        {
            if (_isListening) return;
            _turnIsPushToTalk = reason.StartsWith("Push", StringComparison.Ordinal);
            _turnIsWakeWord = reason.StartsWith("Wake Word", StringComparison.Ordinal);
            _isListening = true;
        }
        Interlocked.Increment(ref _currentTurnId);
        lock (_captureLock)
        {
            try
            {
                _ttsCts?.Cancel();
                _ttsCts?.Dispose();
                _ttsCts = null;
                _activeSoundPlayer?.Stop();
                _activeSoundPlayer?.Dispose();
                _activeSoundPlayer = null;
            }
            catch { }
        }
        StartAudioCapture(); // WASAPI stays on for VAD auto-stop + mic meter even in local mode
        if (IsLocalModel) StartLocalTranscription(); // SAPI dictation = offline local speech-to-text
        Program.Log($"VoiceAssistantService listening started: {reason}");

        _uiShown = false;
        if (uiDelayMs <= 0)
        {
            _uiShown = true;
            ListeningStateChanged?.Invoke(true, reason);
        }
        else
        {
            int turn = _currentTurnId;
            _ = Task.Delay(uiDelayMs).ContinueWith(_ =>
            {
                if (_isListening && turn == _currentTurnId && !_ctrlHadOtherKeys)
                {
                    _uiShown = true;
                    ListeningStateChanged?.Invoke(true, reason);
                }
            }, TaskScheduler.Default);
        }

        _listeningTimeoutCts?.Cancel();
        _listeningTimeoutCts = new CancellationTokenSource();
        var token = _listeningTimeoutCts.Token;

        // Failsafe auto-timeout after 12 seconds of continuous recording
        Task.Delay(12000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled && _isListening && !_ctrlDown)
            {
                Program.Log("VoiceAssistantService session timed out (12s limit).");
                StopListening();
            }
        }, TaskScheduler.Default);
    }

    public void StopListening()
    {
        lock (_listeningStateLock)
        {
            if (!_isListening) return;
            _isListening = false;
        }
        _listeningTimeoutCts?.Cancel();
        Program.Log("VoiceAssistantService listening stopped.");

        string localTranscript = IsLocalModel ? StopLocalTranscription() : "";
        if (_uiShown) ListeningStateChanged?.Invoke(false, "Processing");
        int turnId = _currentTurnId;
        _ = Task.Run(() => FinishTurnAsync(turnId, localTranscript));
    }

    private void CancelWakeWordSession()
    {
        lock (_listeningStateLock)
        {
            if (!_isListening || !_turnIsWakeWord) return;
            _isListening = false;
        }
        _listeningTimeoutCts?.Cancel();
        Interlocked.Increment(ref _currentTurnId);
        _ = StopAudioCaptureAsync();
        if (_uiShown) ListeningStateChanged?.Invoke(false, "Cancel");
        _uiShown = false;
        Program.Log("Wake-word session cancelled because wake-word listening was disabled.");
    }

    private void StartAudioCapture(bool retriedDefault = false)
    {
        string? apiKey = ReadGeminiApiKey();
        if (!IsLocalModel && string.IsNullOrWhiteSpace(apiKey))
        {
            Program.Log($"Gemini API key is missing. Place gemini.key in app directory or set GEMINI_API_KEY environment variable.");
            SpeakAndNotify("Gemini key missing", "Gemini API key not found. Please add your gemini.key file.");
            return;
        }

        WasapiCapture? capture = null;
        try
        {
            capture = CreateCaptureDevice();
            var buffer = new MemoryStream();
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _lastSpeechUtc = DateTime.UtcNow;
            _speechDetectedInSession = false;
            _noiseFloor = 1f;

            capture.DataAvailable += (_, e) =>
            {
                lock (_captureLock)
                {
                    if (_audioBuffer == null || e.BytesRecorded <= 0 || !ReferenceEquals(_audioCapture, capture)) return;

                    if (_audioBuffer.Length + e.BytesRecorded <= MaximumAudioBytes)
                    {
                        _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                        // Voice Activity & Silence Detection (threshold adapts to room noise + mic sensitivity)
                        float rms = CalculateRms(e.Buffer, e.BytesRecorded, capture.WaveFormat);
                        AudioLevelChanged?.Invoke(rms);

                        _noiseFloor = Math.Min(_noiseFloor, Math.Max(rms, 0.0005f));
                        float sensFactor = Math.Max(0.2f, (100 - _microphoneSensitivity) / 50.0f);
                        float speechCutoff = Math.Clamp(_noiseFloor * 3f * sensFactor, 0.004f, 0.04f);

                        if (rms > speechCutoff)
                        {
                            _lastSpeechUtc = DateTime.UtcNow;
                            _speechDetectedInSession = true;
                        }
                        else if (_speechDetectedInSession && !_ctrlDown && _isListening)
                        {
                            // User finished speaking and stayed quiet -> auto-submit (1.1s keeps natural pauses intact)
                            if (DateTime.UtcNow - _lastSpeechUtc > TimeSpan.FromMilliseconds(1100))
                            {
                                Program.Log("Silence detected after speech (1100ms); auto-stopping recording.");
                                Task.Run(StopListening);
                            }
                        }
                    }
                }
            };

            capture.RecordingStopped += (_, e) =>
            {
                lock (_captureLock)
                {
                    _captureStopped?.TrySetResult(e.Exception == null);
                }
            };

            lock (_captureLock)
            {
                _audioCapture = capture;
                _audioBuffer = buffer;
                _captureStopped = stopped;
            }

            capture.StartRecording();
            Program.Log($"Gemini microphone capture started ({capture.WaveFormat.SampleRate}Hz, {capture.WaveFormat.Channels}ch, {capture.WaveFormat.BitsPerSample}bit).");
        }
        catch (Exception ex)
        {
            try { capture?.Dispose(); } catch { }
            lock (_captureLock)
            {
                _audioCapture = null;
                _audioBuffer?.Dispose();
                _audioBuffer = null;
                _captureStopped = null;
            }
            Program.Log($"Gemini microphone capture failed: {ex.Message}");
            // Saved mic may be unplugged/blocked: fall back to Windows default once instead of going deaf.
            if (!retriedDefault && !string.IsNullOrEmpty(_microphoneDeviceId))
            {
                Program.Log("Retrying capture with default microphone.");
                _microphoneDeviceId = "";
                StartAudioCapture(true);
            }
        }
    }

    private static float CalculateRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded <= 0) return 0f;
        double sum = 0;
        int sampleCount = 0;

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible wfe && wfe.SubFormat == SubTypeIeeeFloat);

        if (isFloat && format.BitsPerSample == 32)
        {
            int count = bytesRecorded / 4;
            for (int i = 0; i < count; i++)
            {
                float s = BitConverter.ToSingle(buffer, i * 4);
                sum += s * s;
                sampleCount++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            int count = bytesRecorded / 2;
            for (int i = 0; i < count; i++)
            {
                short s = BitConverter.ToInt16(buffer, i * 2);
                float f = s / 32768.0f;
                sum += f * f;
                sampleCount++;
            }
        }

        return sampleCount == 0 ? 0f : (float)Math.Sqrt(sum / sampleCount);
    }

    private const int MinSpeechPcmBytes = 8000; // 0.25s @ 16kHz mono 16-bit

    private async Task FinishTurnAsync(int turnId, string localTranscript = "")
    {
        if (turnId != _currentTurnId) return;
        bool silent = false;
        try
        {
            if (IsLocalModel)
            {
                var (localCapturedAudio, localCapturedFormat) = await StopAudioCaptureAsync().ConfigureAwait(false);
                var localStopped = _localRecognitionStopped;
                if (localStopped != null)
                {
                    try
                    {
                        if (!await localStopped.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false))
                            throw new TimeoutException("SAPI did not release microphone input.");
                    }
                    catch (TimeoutException)
                    {
                        Program.Log("SAPI stop timed out; disposing recognizer to release microphone.");
                        lock (_wakeReloadLock)
                        {
                            try { _speechEngine?.Dispose(); } catch { }
                            _speechEngine = null;
                            _localRecognitionStopped = null;
                        }
                    }
                }

                string text = localTranscript.Trim();
                if (text.Length < 2 && _turnIsPushToTalk && _speechDetectedInSession && localCapturedAudio != null && localCapturedFormat != null)
                {
                    Program.Log("Live SAPI returned no text; retrying captured push-to-talk audio offline.");
                    text = TranscribePttAudioOffline(localCapturedAudio, localCapturedFormat);
                }
                if (text.Length < 2)
                {
                    if (_speechDetectedInSession) Program.Log("Local turn: speech detected but SAPI returned no text.");
                    if (_turnIsPushToTalk && _speechDetectedInSession && localCapturedAudio != null && localCapturedFormat != null && !string.IsNullOrWhiteSpace(ReadGeminiApiKey()))
                    {
                        Program.Log("Offline transcription empty; sending held push-to-talk audio to Gemini per user approval.");
                        byte[] fallbackPcm = NormalizePcm(ConvertTo16kHzMonoPcm(localCapturedAudio, localCapturedAudio.Length, localCapturedFormat));
                        if (fallbackPcm.Length >= MinSpeechPcmBytes)
                        {
                            _lastErrorWasQuotaExceeded = false;
                            if (!await ProcessAudioWithGeminiAsync(WrapPcmWithWavHeader(fallbackPcm)).ConfigureAwait(false))
                                SpeakAndNotify(_lastErrorWasQuotaExceeded ? "Gemini Quota Exceeded" : "Voice command failed", _lastErrorWasQuotaExceeded ? "Gemini free tier daily limit reached." : "Could not process voice command. Check connection and try again.");
                            return;
                        }
                    }
                    silent = true;
                    SpeakAndNotify("I didn't catch that", "Speech was heard, but no words were recognized. Try again.");
                    return;
                }
                Program.Log($"Processing voice with local model: \"{text}\"");
                CommandRecognized?.Invoke(text);
                if (!await ProcessTextWithLocalModelAsync(text).ConfigureAwait(false))
                {
                    Program.Log("Local model request failed; falling back to built-in command parser.");
                    var intent = IslandLogic.ParseVoiceIntent(text);
                    if (intent != null) ExecuteLocalIntent(intent);
                    else SpeakAndNotify("I didn't catch that", "I didn't catch that. Please try again.");
                }
                return;
            }
            if (_turnIsPushToTalk) await Task.Delay(150).ConfigureAwait(false); // catch the end of the last word
            var (rawAudio, format) = await StopAudioCaptureAsync().ConfigureAwait(false);
            if (rawAudio == null || format == null) { silent = true; return; }

            byte[] pcm = NormalizePcm(ConvertTo16kHzMonoPcm(rawAudio, rawAudio.Length, format));

            // Ctrl shortcut / false wake word / nothing said -> no request, no "didn't catch that".
            bool heardSomething = _speechDetectedInSession || (_turnIsPushToTalk && pcm.Length >= 32000);
            if (!heardSomething || pcm.Length < MinSpeechPcmBytes) { silent = true; return; }

            byte[] wav = WrapPcmWithWavHeader(pcm);
            Program.Log($"Processing voice via Gemini ({wav.Length} bytes)...");
            _lastErrorWasQuotaExceeded = false;
            if (!await ProcessAudioWithGeminiAsync(wav).ConfigureAwait(false))
            {
                if (_lastErrorWasQuotaExceeded)
                    SpeakAndNotify("Gemini Quota Exceeded", "Gemini free tier daily limit reached.");
                else
                    SpeakAndNotify("I didn't catch that", "I didn't catch that. Please try again.");
            }
        }
        catch (Exception ex)
        {
            Program.Log($"FinishTurnAsync error: {ex.Message}");
            SpeakAndNotify("Gemini voice error", "Gemini request failed. Check your connection.");
        }
        finally
        {
            if (turnId == _currentTurnId && !_isListening)
                ListeningStateChanged?.Invoke(false, silent ? "Cancel" : "Idle");
        }
    }

    /// <summary>Boost quiet mic audio so Gemini can hear it (peak-normalize, max 8x). In place.</summary>
    public static byte[] NormalizePcm(byte[] pcm)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, i)));
        if (peak < 100) return pcm; // silence: do not amplify noise

        float gain = Math.Min(24000f / peak, 8f);
        if (gain <= 1.05f) return pcm;

        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            short s = (short)Math.Clamp((int)(BitConverter.ToInt16(pcm, i) * gain), short.MinValue, short.MaxValue);
            pcm[i] = (byte)(s & 0xFF);
            pcm[i + 1] = (byte)((s >> 8) & 0xFF);
        }
        return pcm;
    }

    private async Task<(byte[]? Audio, WaveFormat? Format)> StopAudioCaptureAsync()
    {
        WasapiCapture? capture;
        byte[]? rawAudio;
        TaskCompletionSource<bool>? stopped;
        lock (_captureLock)
        {
            capture = _audioCapture;
            rawAudio = _audioBuffer?.ToArray();
            _audioBuffer?.Dispose();
            _audioBuffer = null;
            _audioCapture = null;
            stopped = _captureStopped;
            _captureStopped = null;
        }

        if (capture == null) return (null, null);
        WaveFormat format = capture.WaveFormat;
        try
        {
            await Task.Run(capture.StopRecording).ConfigureAwait(false);
            if (stopped != null && !await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false))
                Program.Log("Microphone capture stopped with an error.");
        }
        catch (TimeoutException) { Program.Log("Microphone stop event timed out; disposing capture device."); }
        catch (Exception ex) { Program.Log($"Microphone stop failed: {ex.Message}"); }
        finally
        {
            try { await Task.Run(capture.Dispose).ConfigureAwait(false); }
            catch (Exception ex) { Program.Log($"Microphone dispose failed: {ex.Message}"); }
        }
        Program.Log("WASAPI microphone capture released.");
        return (rawAudio, format);
    }
    /// <summary>
    /// Resamples and downmixes raw audio into standard 16 kHz Mono 16-bit signed PCM bytes.
    /// Handles 32-bit float, 16/24/32-bit PCM, multi-channel microphone arrays, and sample rate conversion.
    /// </summary>
    public static byte[] ConvertTo16kHzMonoPcm(byte[] rawAudio, int bytesRecorded, WaveFormat format)
    {
        if (rawAudio == null || bytesRecorded <= 0 || format == null) return Array.Empty<byte>();

        int bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        int channels = Math.Max(1, format.Channels);
        int frameBytes = channels * bytesPerSample;
        if (frameBytes <= 0) return Array.Empty<byte>();

        int totalFrames = bytesRecorded / frameBytes;
        if (totalFrames == 0) return Array.Empty<byte>();

        // 1. Downmix all channels to mono float [-1.0f, 1.0f]
        float[] mono = new float[totalFrames];
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible wfe && wfe.SubFormat == SubTypeIeeeFloat);

        for (int f = 0; f < totalFrames; f++)
        {
            int frameOffset = f * frameBytes;
            double sum = 0;

            for (int c = 0; c < channels; c++)
            {
                int offset = frameOffset + (c * bytesPerSample);
                float sample = 0f;

                if (isFloat && format.BitsPerSample == 32)
                {
                    sample = BitConverter.ToSingle(rawAudio, offset);
                }
                else if (format.BitsPerSample == 16)
                {
                    sample = BitConverter.ToInt16(rawAudio, offset) / 32768.0f;
                }
                else if (format.BitsPerSample == 24)
                {
                    int val = (rawAudio[offset] << 8) | (rawAudio[offset + 1] << 16) | ((sbyte)rawAudio[offset + 2] << 24);
                    sample = val / 2147483648.0f;
                }
                else if (format.BitsPerSample == 32)
                {
                    sample = BitConverter.ToInt32(rawAudio, offset) / 2147483648.0f;
                }

                sum += sample;
            }

            mono[f] = (float)(sum / channels);
        }

        // 2. Resample mono to 16,000 Hz with linear interpolation
        int inRate = format.SampleRate > 0 ? format.SampleRate : 48000;
        const int targetRate = 16000;
        int outFrames = (int)Math.Round((long)totalFrames * (double)targetRate / inRate);
        if (outFrames <= 0) return Array.Empty<byte>();

        byte[] pcmData = new byte[outFrames * 2];
        double step = (double)inRate / targetRate;

        for (int i = 0; i < outFrames; i++)
        {
            double srcPos = i * step;
            int idx0 = (int)srcPos;
            int idx1 = Math.Min(idx0 + 1, totalFrames - 1);
            double frac = srcPos - idx0;

            float val = idx0 < totalFrames
                ? (float)((1.0 - frac) * mono[idx0] + frac * mono[idx1])
                : 0f;

            short pcmVal = (short)Math.Clamp((int)Math.Round(val * 32767.0f), -32768, 32767);
            pcmData[i * 2] = (byte)(pcmVal & 0xFF);
            pcmData[i * 2 + 1] = (byte)((pcmVal >> 8) & 0xFF);
        }

        return pcmData;
    }

    /// <summary>
    /// Wraps raw Mono 16-bit signed PCM data into a standard 44-byte RIFF/WAVE header container.
    /// </summary>
    public static byte[] WrapPcmWithWavHeader(byte[] pcmData, int sampleRate = 16000)
    {
        if (pcmData == null || pcmData.Length == 0) return Array.Empty<byte>();
        using var ms = new MemoryStream(44 + pcmData.Length);
        using var writer = new BinaryWriter(ms);

        // RIFF chunk
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + pcmData.Length);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt chunk
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16); // subchunk1 size
        writer.Write((short)1); // PCM format
        writer.Write((short)1); // 1 channel
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // ByteRate: sampleRate * 2
        writer.Write((short)2); // BlockAlign: 2
        writer.Write((short)16); // BitsPerSample: 16

        // data chunk
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return ms.ToArray();
    }

    /// <summary>
    /// Converts any hardware audio buffer into standard 16 kHz Mono 16-bit signed PCM RIFF/WAVE.
    /// Handles 32-bit float, 16/24/32-bit PCM, multi-channel microphone arrays, and sample rate conversion.
    /// </summary>
    public static byte[] ConvertTo16kHzMonoWav(byte[] rawAudio, WaveFormat format)
    {
        if (rawAudio == null || rawAudio.Length == 0 || format == null) return Array.Empty<byte>();
        byte[] pcm = ConvertTo16kHzMonoPcm(rawAudio, rawAudio.Length, format);
        return WrapPcmWithWavHeader(pcm);
    }

    private async Task<bool> ProcessAudioWithGeminiAsync(byte[] wavBytes)
    {
        if (string.IsNullOrWhiteSpace(ReadGeminiApiKey())) return false;

        for (int mi = 0; mi < CandidateModels.Length; mi++)
        {
            string model = CandidateModels[mi];
            string apiKey = ReadGeminiApiKey()!;   // re-read: may have rotated
            if (_modelThrottledUntil.TryGetValue(model, out var until) && DateTime.UtcNow < until)
            {
                continue; // Skip rate-limited model immediately without delay
            }
            NoteKeyUse(apiKey);

            try
            {
                var payload = new
                {
                    system_instruction = new
                    {
                        parts = new[]
                        {
                            new { text = NixiSystemPrompt }
                        }
                    },
                    generationConfig = new
                    {
                        thinkingConfig = new { thinkingBudget = 0 },
                        maxOutputTokens = 120,
                        temperature = 0.0
                    },
                    tool_config = new { function_calling_config = new { mode = "ANY" } },
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "audio/wav",
                                        data = Convert.ToBase64String(wavBytes)
                                    }
                                },
                                new
                                {
                                    text = "Process user voice command."
                                }
                            }
                        }
                    },
                    tools = GeminiTools
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{GeminiApiBase}/models/{model}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var reqCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 5s was too tight for slow links / cold TLS
                using var response = await _httpClient.SendAsync(request, reqCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    bool badKey = (int)response.StatusCode is 400 or 401 or 403 && err.Contains("API key", StringComparison.OrdinalIgnoreCase);
                    if ((int)response.StatusCode == 429)
                    {
                        _lastErrorWasQuotaExceeded = true;
                        if (RotateKey(apiKey)) { mi--; Program.Log("Gemini 429: retrying same model on next key."); }   // seamless: ~1 round trip
                        else
                        {
                            _modelThrottledUntil[model] = DateTime.UtcNow.AddMinutes(2);
                            Program.Log($"Gemini model {model} throttled (HTTP 429, no spare key). Backing off for 2 minutes.");
                        }
                    }
                    else if (badKey)
                    {
                        if (RotateKey(apiKey, benchMinutes: 60)) mi--;   // dead key: bench 1h, retry on next
                    }
                    else if ((int)response.StatusCode is 400 or 403 or 404)
                    {
                        _modelThrottledUntil[model] = DateTime.UtcNow.AddHours(6); // wrong/unavailable model: skip instantly next time
                    }
                    Program.Log($"Gemini model {model} returned HTTP {(int)response.StatusCode}: {TruncateApiError(err)}");
                    continue; // Try next fast candidate
                }

                string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return HandleGeminiResponse(responseJson);
            }
            catch (OperationCanceledException)
            {
                Program.Log($"Gemini model {model} timed out after 10 seconds. Trying next candidate.");
            }
            catch (Exception ex)
            {
                Program.Log($"Gemini request error on model {model}: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// Local model turn: send the SAPI transcript to Ollama's OpenAI-compatible endpoint with tool
    /// declarations; execute the returned tool call. Returns false when the server/model is unavailable.
    /// </summary>
    private async Task<bool> ProcessTextWithLocalModelAsync(string transcript)
    {
        try
        {
            var payload = new
            {
                model = _localModel,
                messages = new object[]
                {
                    new { role = "system", content = NixiLocalSystemPrompt },
                    new { role = "user", content = transcript }
                },
                tools = LocalTools,
                tool_choice = "auto",
                temperature = 0.0,
                max_tokens = 160,
                stream = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_localEndpoint.TrimEnd('/')}/chat/completions");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // CPU inference can be slow on the first token
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                Program.Log($"Local model HTTP {(int)response.StatusCode}: {TruncateApiError(err)}");
                return false;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return false;
            var message = choices[0].GetProperty("message");

            if (message.TryGetProperty("tool_calls", out var calls) && calls.GetArrayLength() > 0)
            {
                var function = calls[0].GetProperty("function");
                string funcName = function.GetProperty("name").GetString() ?? "";
                string argsJson = function.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString() ?? "{}"
                    : "{}";
                using var argsDoc = JsonDocument.Parse(argsJson);
                ExecuteGeminiTool(funcName, argsDoc.RootElement); // caller already raised CommandRecognized with the transcript
                return true;
            }

            if (message.TryGetProperty("content", out var content) && !string.IsNullOrWhiteSpace(content.GetString()))
            {
                string reply = content.GetString()!.Trim();
                SpeakAndNotify(reply, reply);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Program.Log($"Local model error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Makes the local model runnable: starts `ollama serve` when the server is down and pulls the
    /// configured model when it is missing. Safe to call on every switch to local mode.
    /// </summary>
    public async Task<string> EnsureLocalModelReadyAsync()
    {
        try
        {
            string baseUrl = _localEndpoint.TrimEnd('/');
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl = baseUrl[..^3];

            bool up = await ProbeOllamaAsync(baseUrl).ConfigureAwait(false);
            if (!up)
            {
                string? exe = FindOllamaExe();
                if (exe == null) return "Ollama not found on this PC. Install it from ollama.com, then switch again.";
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "serve",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    Program.Log("Started 'ollama serve'.");
                }
                catch (Exception ex) { return $"Could not start Ollama: {ex.Message}"; }

                for (int i = 0; i < 15 && !up; i++)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    up = await ProbeOllamaAsync(baseUrl).ConfigureAwait(false);
                }
                if (!up) return "Ollama server did not come up on " + baseUrl + ".";
            }

            using (var tags = await GetOllamaJsonAsync(baseUrl + "/api/tags").ConfigureAwait(false))
            {
                if (tags != null && tags.RootElement.TryGetProperty("models", out var models))
                {
                    foreach (var m in models.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var n) &&
                            string.Equals(n.GetString(), _localModel, StringComparison.OrdinalIgnoreCase))
                            return $"Local model '{_localModel}' is ready.";
                    }
                }
            }

            string? pullExe = FindOllamaExe();
            if (pullExe == null) return $"Model '{_localModel}' is not installed locally.";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pullExe,
                    Arguments = $"pull \"{_localModel}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                Program.Log($"Pulling local model '{_localModel}'...");
            }
            catch (Exception ex) { return $"Could not pull model: {ex.Message}"; }
            return $"Downloading '{_localModel}' (one-time). Voice commands use the built-in parser meanwhile.";
        }
        catch (Exception ex)
        {
            Program.Log($"EnsureLocalModelReadyAsync error: {ex.Message}");
            return "Local model check failed: " + ex.Message;
        }
    }

    private async Task<bool> ProbeOllamaAsync(string baseUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var resp = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/tags"), cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<JsonDocument?> GetOllamaJsonAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var resp = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            string json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    private static string? FindOllamaExe()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
        ];
        foreach (string c in candidates) if (File.Exists(c)) return c;
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
                foreach (string dir in pathEnv.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string c = Path.Combine(dir.Trim(), "ollama.exe");
                    if (File.Exists(c)) return c;
                }
        }
        catch { }
        return null;
    }

    private string TranscribePttAudioOffline(byte[] rawAudio, WaveFormat format)
    {
        try
        {
            byte[] wav = ConvertTo16kHzMonoWav(rawAudio, format);
            if (wav.Length == 0) return "";
            using var stream = new MemoryStream(wav);
            using var recognizer = new SpeechRecognitionEngine();
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToWaveStream(stream);
            string text = recognizer.Recognize(TimeSpan.FromSeconds(8))?.Text?.Trim() ?? "";
            Program.Log($"Offline push-to-talk transcript: '{text}'.");
            return text;
        }
        catch (Exception ex)
        {
            Program.Log($"Offline push-to-talk transcription failed: {ex.Message}");
            return "";
        }
    }
    /// <summary>Swap the wake-word grammar for a dictation grammar so SAPI transcribes locally (offline).</summary>
    private void StartLocalTranscription()
    {
        var engine = _speechEngine;
        if (engine == null) { Program.Log("Local mode: SAPI engine not ready yet; transcript will be empty."); return; }
        lock (_wakeReloadLock)
        {
            try
            {
                lock (_dictation) _dictation.Clear();
                _transcribing = true;
                _localRecognitionStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_wakeWordEnabled) engine.RecognizeAsyncCancel();
                else engine.SetInputToDefaultAudioDevice(); // Open mic only for active push-to-talk transcription.
                foreach (var g in engine.Grammars.ToArray())
                    if (g.Name is "WakeWord" or "WakeGarbage") engine.UnloadGrammar(g);
                var dict = new DictationGrammar();
                engine.LoadGrammar(dict);
                _dictationGrammar = dict;
                engine.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex)
            {
                _transcribing = false;
                _localRecognitionStopped = null;
                Program.Log($"StartLocalTranscription failed: {ex.Message}");
            }
        }
    }

    /// <summary>Stop dictation, restore the wake-word grammar, and return the collected transcript.</summary>
    private string StopLocalTranscription()
    {
        string text;
        lock (_dictation) { text = _dictation.ToString().Trim(); _dictation.Clear(); }
        _transcribing = false;

        var engine = _speechEngine;
        if (engine == null) return text;
        lock (_wakeReloadLock)
        {
            try
            {
                engine.RecognizeAsyncCancel();
                if (_dictationGrammar != null)
                {
                    try { engine.UnloadGrammar(_dictationGrammar); } catch { }
                    _dictationGrammar = null;
                }
                { engine.LoadGrammar(BuildWakeGrammar()); engine.LoadGrammar(BuildGarbageGrammar()); }
                if (_wakeWordEnabled) engine.RecognizeAsync(RecognizeMode.Multiple);
            }
            catch (Exception ex) { Program.Log($"StopLocalTranscription failed: {ex.Message}"); }
        }
        return text;
    }

    private bool HandleGeminiResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return false;

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
                return false;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var functionCall))
                {
                    string funcName = functionCall.GetProperty("name").GetString() ?? "";
                    var args = functionCall.GetProperty("args");
                    string transcript = "";
                    if (args.TryGetProperty("transcript", out var tElem))
                        transcript = tElem.GetString() ?? "";

                    if (!string.IsNullOrWhiteSpace(transcript))
                    {
                        CommandRecognized?.Invoke(transcript);
                    }

                    ExecuteGeminiTool(funcName, args);
                    return true;
                }

                if (part.TryGetProperty("text", out var textElem))
                {
                    string text = textElem.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        SpeakAndNotify(text, text);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log($"HandleGeminiResponse error: {ex.Message}");
        }

        return false;
    }

    private void ExecuteGeminiTool(string toolName, JsonElement args)
    {
        switch (toolName)
        {
            case "open_application":
            {
                string app = args.TryGetProperty("app_name", out var a) ? a.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(app)) break;

                string title = char.ToUpperInvariant(app.Trim()[0]) + app.Trim()[1..].ToLowerInvariant();
                if (AppFinder.TryLaunch(app))
                    SpeakAndNotify($"Opened {title}.", $"Opening {title}.");
                else
                    SpeakAndNotify($"{title}: not found.", "App not found.");
                break;
            }

            case "close_application":
            {
                string app = args.TryGetProperty("app_name", out var a) ? a.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(app)) break;
                string title = char.ToUpperInvariant(app.Trim()[0]) + app.Trim()[1..].ToLowerInvariant();
                if (AppFinder.Close(app) > 0)
                    SpeakAndNotify($"Closed {title}.", $"Closed {title}.");
                else
                    SpeakAndNotify($"{title}: not running.", "App not found.");
                break;
            }

            case "control_media":
            {
                string action = (args.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "").ToLowerInvariant().Trim();
                ExecuteMediaAction(action);
                break;
            }

            case "set_volume":
            {
                int percent = args.TryGetProperty("percent", out var p) && p.TryGetInt32(out int val) ? Math.Clamp(val, 0, 100) : 50;
                if (SetVolumeAction != null)
                {
                    SetVolumeAction(percent / 100f);
                }
                else
                {
                    SetSystemVolumeDirect(percent / 100f);
                }
                SpeakAndNotify($"Volume set to {percent}%.", $"Volume set to {percent} percent.");
                break;
            }

            case "take_screenshot":
            {
                var (ok, path) = CaptureScreenshot();
                if (ok && !string.IsNullOrWhiteSpace(path))
                {
                    SpeakAndNotify("Screenshot saved to Pictures.", "Screenshot captured and copied to clipboard.");
                }
                else
                {
                    SpeakAndNotify("Screenshot copied to clipboard.", "Screenshot copied to clipboard.");
                }
                break;
            }

            case "adjust_brightness":
            {
                int percent = args.TryGetProperty("percent", out var p) && p.TryGetInt32(out int val) ? val : -1;
                string dir = (args.TryGetProperty("direction", out var d) ? d.GetString() ?? "" : "").ToLowerInvariant().Trim();

                int targetBrightness;
                int current = GetCurrentBrightness();

                if (percent >= 0)
                {
                    targetBrightness = Math.Clamp(percent, 0, 100);
                }
                else if (dir is "increase" or "up" or "brighter")
                {
                    targetBrightness = Math.Clamp(current + 15, 0, 100);
                }
                else if (dir is "decrease" or "down" or "dim")
                {
                    targetBrightness = Math.Clamp(current - 15, 0, 100);
                }
                else
                {
                    targetBrightness = current;
                }

                SetScreenBrightness(targetBrightness);
                SpeakAndNotify($"Brightness set to {targetBrightness}%.", $"Brightness {targetBrightness} percent.");
                break;
            }

            case "search_web":
            {
                string query = args.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(query))
                {
                    string url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
                    LaunchProcess(url);
                    SpeakAndNotify($"Searching Google for \"{query}\"", $"Searching for {query}.");
                }
                break;
            }

            case "get_system_info":
            {
                string infoType = (args.TryGetProperty("info_type", out var it) ? it.GetString() ?? "" : "").ToLowerInvariant().Trim();
                if (infoType.Contains("date"))
                {
                    string d = DateTime.Now.ToString("dddd, MMMM d");
                    SpeakAndNotify($"Today is {d}", $"Today is {d}.");
                }
                else if (infoType.Contains("battery"))
                {
                    var power = System.Windows.Forms.SystemInformation.PowerStatus;
                    int batt = (int)Math.Round(power.BatteryLifePercent * 100.0);
                    string status = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "plugged in and charging" : "on battery";
                    SpeakAndNotify($"Battery: {batt}% ({status})", $"Battery is at {batt} percent, {status}.");
                }
                else // default time
                {
                    string t = DateTime.Now.ToString("t");
                    SpeakAndNotify($"Current time is {t}", $"It is {t}.");
                }
                break;
            }

            case "conversation_reply":
            {
                string reply = args.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    SpeakAndNotify(reply, reply);
                }
                break;
            }

            default:
            {
                Program.Log($"Unknown Gemini tool called: {toolName}");
                break;
            }
        }
    }

    private void ExecuteLocalIntent(VoiceIntent intent)
    {
        try
        {
            switch (intent.Type)
            {
                case VoiceActionType.LaunchApp:
                    if (!string.IsNullOrWhiteSpace(intent.Target))
                    {
                        if (IslandLogic.TryResolveAppTarget(intent.Target, out string target, out string title))
                        {
                            LaunchProcess(target);
                            SpeakAndNotify(intent.DisplaySummary, intent.ReplyVoice);
                        }
                        else
                        {
                            LaunchProcess(intent.Target);
                            SpeakAndNotify(intent.DisplaySummary, intent.ReplyVoice);
                        }
                    }
                    break;

                case VoiceActionType.CloseApp:
                    if (!string.IsNullOrWhiteSpace(intent.Target))
                    {
                        int closed = AppFinder.Close(intent.Target);
                        if (closed > 0)
                        {
                            SpeakAndNotify($"Closed {closed} app(s).", $"Closed {closed} app(s).");
                        }
                        else
                        {
                            SpeakAndNotify($"{intent.Target}: not running.", "App not found.");
                        }
                    }
                    break;

                case VoiceActionType.SearchWeb:
                case VoiceActionType.OpenUrl:
                    if (!string.IsNullOrWhiteSpace(intent.Target))
                    {
                        LaunchProcess(intent.Target);
                        SpeakAndNotify(intent.DisplaySummary, intent.ReplyVoice);
                    }
                    break;

                case VoiceActionType.SystemTime:
                case VoiceActionType.SystemDate:
                case VoiceActionType.Greeting:
                case VoiceActionType.Joke:
                    SpeakAndNotify(intent.DisplaySummary, intent.ReplyVoice);
                    break;

                case VoiceActionType.MediaControl:
                    ExecuteMediaAction(intent.Target);
                    break;

                case VoiceActionType.Screenshot:
                    var (ok, path) = CaptureScreenshot();
                    if (ok && !string.IsNullOrWhiteSpace(path))
                    {
                        SpeakAndNotify("Screenshot saved to Pictures.", "Screenshot captured and copied to clipboard.");
                    }
                    else
                    {
                        SpeakAndNotify("Screenshot copied to clipboard.", "Screenshot copied to clipboard.");
                    }
                    break;

                case VoiceActionType.Brightness:
                    int current = GetCurrentBrightness();
                    int targetBrightness;
                    if (intent.Target.StartsWith('+'))
                    {
                        int delta = int.TryParse(intent.Target[1..], out int d) ? d : 15;
                        targetBrightness = Math.Clamp(current + delta, 0, 100);
                    }
                    else if (intent.Target.StartsWith('-'))
                    {
                        int delta = int.TryParse(intent.Target[1..], out int d) ? d : 15;
                        targetBrightness = Math.Clamp(current - delta, 0, 100);
                    }
                    else if (int.TryParse(intent.Target, out int p))
                    {
                        targetBrightness = Math.Clamp(p, 0, 100);
                    }
                    else
                    {
                        targetBrightness = current;
                    }
                    SetScreenBrightness(targetBrightness);
                    SpeakAndNotify($"Brightness set to {targetBrightness}%.", $"Brightness {targetBrightness} percent.");
                    break;

                default:
                    if (!string.IsNullOrWhiteSpace(intent.ReplyVoice))
                    {
                        SpeakAndNotify(intent.DisplaySummary, intent.ReplyVoice);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"ExecuteLocalIntent error: {ex.Message}");
        }
    }

    private void ExecuteMediaAction(string action)
    {
        if (action.StartsWith("volume_") && int.TryParse(action[7..], out int percent))
        {
            float scalar = Math.Clamp(percent / 100f, 0f, 1f);
            if (SetVolumeAction != null)
            {
                SetVolumeAction(scalar);
            }
            else
            {
                SetSystemVolumeDirect(scalar);
            }
            SpeakAndNotify($"Volume set to {percent}%.", $"Volume set to {percent} percent.");
            return;
        }

        if (action is "next" or "skip" or "previous" or "prev" or "back")
        {
            bool next = action is "next" or "skip";
            if (SkipTrackAction?.Invoke(next) == true)
            {
                string m = next ? "Skipped to next track." : "Previous track.";
                SpeakAndNotify(m, m);
                return;
            }
        }

        byte vk = action switch
        {
            "play" or "play_pause" or "resume" => VK_MEDIA_PLAY_PAUSE,
            "pause" or "stop" => VK_MEDIA_PLAY_PAUSE,
            "next" or "skip" => VK_MEDIA_NEXT_TRACK,
            "previous" or "prev" or "back" => VK_MEDIA_PREV_TRACK,
            "mute" or "unmute" => VK_VOLUME_MUTE,
            "volume_up" or "volup" or "louder" => VK_VOLUME_UP,
            "volume_down" or "voldown" or "quieter" => VK_VOLUME_DOWN,
            _ => 0
        };

        if (vk != 0)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        string summary = action switch
        {
            "play" or "play_pause" or "resume" => "Resumed playback.",
            "pause" or "stop" => "Paused playback.",
            "next" or "skip" => "Skipped to next track.",
            "previous" or "prev" or "back" => "Previous track.",
            "mute" => "Muted volume.",
            "unmute" => "Unmuted volume.",
            "volume_up" or "volup" or "louder" => "Volume increased.",
            "volume_down" or "voldown" or "quieter" => "Volume decreased.",
            _ => "Media command sent."
        };

        SpeakAndNotify(summary, summary);
    }

    private static void SetSystemVolumeDirect(float volumeScalar)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device?.AudioEndpointVolume != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volumeScalar, 0f, 1f);
            }
        }
        catch (Exception ex)
        {
            Program.Log($"Direct volume adjustment failed: {ex.Message}");
        }
    }

    public static (bool Success, string FilePath) CaptureScreenshot()
    {
        try
        {
            int left = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
            int top = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
            int width = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
            int height = System.Windows.Forms.SystemInformation.VirtualScreen.Height;

            using var bmp = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
            }

            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string folder = Path.Combine(pictures, "Screenshots");
            Directory.CreateDirectory(folder);
            string filename = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            string filePath = Path.Combine(folder, filename);
            bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            // Copy to Windows Clipboard on an STA thread
            var thread = new Thread(() =>
            {
                try
                {
                    var hBitmap = bmp.GetHbitmap();
                    try
                    {
                        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        System.Windows.Clipboard.SetImage(source);
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"Clipboard SetImage warning: {ex.Message}");
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(1000);

            return (true, filePath);
        }
        catch (Exception ex)
        {
            Program.Log($"Direct screen capture failed: {ex.Message}; triggering Windows screenshot hotkey fallback.");
            keybd_event(VK_SNAPSHOT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SNAPSHOT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            return (false, string.Empty);
        }
    }

    public static int GetCurrentBrightness()
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType != null)
            {
                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic service = locator.ConnectServer(".", "root\\wmi");
                dynamic monitors = service.ExecQuery("SELECT * FROM WmiMonitorBrightness");
                foreach (dynamic m in monitors)
                {
                    try
                    {
                        var prop = m?.Properties_?.Item("CurrentBrightness");
                        if (prop?.Value != null)
                        {
                            return Convert.ToInt32(prop.Value);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log($"GetCurrentBrightness error: {ex.Message}");
        }
        return 50;
    }

    public static bool SetScreenBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        bool success = false;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType != null)
            {
                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic service = locator.ConnectServer(".", "root\\wmi");
                dynamic monitors = service.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (dynamic m in monitors)
                {
                    m.WmiSetBrightness(1, (byte)percent);
                    success = true;
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log($"SetScreenBrightness COM error: {ex.Message}");
        }

        if (!success)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"(Get-CimInstance -Namespace root\\WMI -ClassName WmiMonitorBrightnessMethods).WmiSetBrightness(1, {percent})\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(1500);
                success = true;
            }
            catch (Exception ex)
            {
                Program.Log($"SetScreenBrightness PowerShell fallback error: {ex.Message}");
            }
        }

        return success;
    }

    private static void LaunchProcess(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to launch process '{target}': {ex.Message}");
        }
    }

    // ---- Gemini key rotation: gemini.key may hold several keys, one per line ----
    private static string[]? _keys;
    private static int _keyIdx;
    private static readonly object _keyLock = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _keyLimitedUntil = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Start, int Count)> _keyUse = new();
    private const int PreemptiveRpm = 10;   // ponytail: free tier ~15 req/min; switch before the wall. Tune for paid plans.

    private static string[] Keys() => _keys ??= (ReadGeminiApiKeyUncached() ?? "")
        .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(l => !l.StartsWith('#')).Distinct().ToArray();

    /// <summary>Active key (first one until it is rate limited).</summary>
    public static string? ReadGeminiApiKey() { var k = Keys(); return k.Length == 0 ? null : k[_keyIdx % k.Length]; }

    public static bool HasValidApiKey() => !string.IsNullOrWhiteSpace(ReadGeminiApiKey());

    public static void SaveGeminiApiKey(string key)
    {
        lock (_keyLock)
        {
            key = (key ?? "").Trim();
            if (string.IsNullOrEmpty(key)) return;
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
            Directory.CreateDirectory(appDataDir);
            File.WriteAllText(Path.Combine(appDataDir, "gemini.key"), key);
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gemini.key"), key);
            }
            catch { }
            _keys = null;
            _keyIdx = 0;
            _keyLimitedUntil.Clear();
            _keyUse.Clear();
        }
    }

    /// <summary>Move to a key that is not limited. limited=true also benches <paramref name="bad"/> for 2 min. False = no other key free.</summary>
    private static bool RotateKey(string bad, bool limited = true, int benchMinutes = 2)
    {
        lock (_keyLock)
        {
            var k = Keys();
            if (limited) _keyLimitedUntil[bad] = DateTime.UtcNow.AddMinutes(benchMinutes);
            if (k.Length < 2) return false;
            if (k[_keyIdx % k.Length] != bad) return true;   // another thread already rotated
            for (int i = 1; i < k.Length; i++)
            {
                int j = (_keyIdx + i) % k.Length;
                if (_keyLimitedUntil.TryGetValue(k[j], out var until) && until > DateTime.UtcNow) continue;
                _keyIdx = j;
                Program.Log($"Gemini key rotated to #{j + 1} of {k.Length}.");
                return true;
            }
            return false;
        }
    }

    /// <summary>Count a request on this key; quietly move to the next key before the per-minute limit is hit.</summary>
    private static void NoteKeyUse(string key)
    {
        var now = DateTime.UtcNow;
        var u = _keyUse.AddOrUpdate(key, (now, 1), (_, o) => now - o.Start > TimeSpan.FromMinutes(1) ? (now, 1) : (o.Start, o.Count + 1));
        if (u.Count >= PreemptiveRpm) RotateKey(key, limited: false);
    }

    /// <summary>Called when listening starts: if the active key is benched, switch now so the request later does not wait.</summary>
    private static void EnsureKeyReady()
    {
        string? k = ReadGeminiApiKey();
        if (k != null && _keyLimitedUntil.TryGetValue(k, out var u) && u > DateTime.UtcNow) RotateKey(k, limited: false);
    }

    private static string? ReadGeminiApiKeyUncached()
    {
        // 1. Environment Variable
        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey;

        // 2. AppContext.BaseDirectory
        string baseFile = Path.Combine(AppContext.BaseDirectory, "gemini.key");
        if (File.Exists(baseFile))
        {
            try
            {
                string k = File.ReadAllText(baseFile).Trim();
                if (!string.IsNullOrWhiteSpace(k)) return k;
            }
            catch { }
        }

        // 3. %APPDATA%\DynamicIsland\gemini.key
        string appDataFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DynamicIsland",
            "gemini.key");
        if (File.Exists(appDataFile))
        {
            try
            {
                string k = File.ReadAllText(appDataFile).Trim();
                if (!string.IsNullOrWhiteSpace(k)) return k;
            }
            catch { }
        }

        // 4. Directory Traversal Upwards (during dev / dotnet run / publish)
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "gemini.key");
                if (File.Exists(candidate))
                {
                    string k = File.ReadAllText(candidate).Trim();
                    if (!string.IsNullOrWhiteSpace(k)) return k;
                }

                string pubCandidate = Path.Combine(dir.FullName, "publish", "gemini.key");
                if (File.Exists(pubCandidate))
                {
                    string k = File.ReadAllText(pubCandidate).Trim();
                    if (!string.IsNullOrWhiteSpace(k)) return k;
                }

                string srcCandidate = Path.Combine(dir.FullName, "src", "gemini.key");
                if (File.Exists(srcCandidate))
                {
                    string k = File.ReadAllText(srcCandidate).Trim();
                    if (!string.IsNullOrWhiteSpace(k)) return k;
                }

                dir = dir.Parent;
            }
        }
        catch { }

        return null;
    }

    private static string TruncateApiError(string error) =>
        error.Length <= 800 ? error : error[..800];

    private void SpeakAndNotify(string displaySummary, string spokenText)
    {
        ActionExecuted?.Invoke(displaySummary, spokenText);
        if (string.IsNullOrWhiteSpace(spokenText)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                CancellationToken ct;
                lock (_captureLock)
                {
                    _ttsCts?.Cancel();
                    _ttsCts?.Dispose();
                    _ttsCts = new CancellationTokenSource();
                    ct = _ttsCts.Token;

                    try
                    {
                        _activeSoundPlayer?.Stop();
                        _activeSoundPlayer?.Dispose();
                        _activeSoundPlayer = null;
                    }
                    catch { }
                }

                bool played = false;
                string? apiKey = ReadGeminiApiKey();
                if (!IsLocalModel && !string.IsNullOrWhiteSpace(apiKey))
                {
                    played = await TryPlayNaturalGeminiFemaleVoiceAsync(spokenText, apiKey, ct).ConfigureAwait(false);
                }

                if (!played && !ct.IsCancellationRequested)
                {
                    played = await TryPlaySapiFemaleVoiceAsync(spokenText, ct).ConfigureAwait(false);
                }

                if (!played && !ct.IsCancellationRequested)
                {
                    Program.Log("Voice playback unavailable; displaying on island.");
                }
            }
            catch (Exception ex)
            {
                Program.Log($"SpeakAndNotify error: {ex.Message}");
            }
        });
    }

    private static void SelectBestFemaleVoice(System.Speech.Synthesis.SpeechSynthesizer synth)
    {
        foreach (var voice in synth.GetInstalledVoices())
        {
            if (voice.Enabled && voice.VoiceInfo.Gender == System.Speech.Synthesis.VoiceGender.Female)
            {
                synth.SelectVoice(voice.VoiceInfo.Name);
                return;
            }
        }
        string[] preferredNames = ["Microsoft Zira Desktop", "Microsoft Zira", "Microsoft Jenny", "Microsoft Eva"];
        foreach (var name in preferredNames)
        {
            try { synth.SelectVoice(name); return; } catch { }
        }
        try { synth.SelectVoiceByHints(System.Speech.Synthesis.VoiceGender.Female, System.Speech.Synthesis.VoiceAge.Adult); } catch { }
    }

    private async Task<bool> TryPlaySapiFemaleVoiceAsync(string text, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                SelectBestFemaleVoice(synth);
                using var ms = new MemoryStream();
                synth.SetOutputToWaveStream(ms);
                synth.Speak(text);
                ms.Position = 0;
                using var player = new System.Media.SoundPlayer(ms);
                lock (_captureLock)
                {
                    if (ct.IsCancellationRequested) return false;
                    try { _activeSoundPlayer?.Stop(); _activeSoundPlayer?.Dispose(); } catch { }
                    _activeSoundPlayer = player;
                }
                player.PlaySync();
                lock (_captureLock)
                {
                    if (_activeSoundPlayer == player) _activeSoundPlayer = null;
                }
                return true;
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return true; }
        catch (Exception ex)
        {
            Program.Log($"SAPI female voice error: {ex.Message}");
            return false;
        }
    }

    private static readonly SemaphoreSlim _ttsGate = new(1, 1);
    private static readonly string[] CandidateTtsModels = ["gemini-2.5-flash-preview-tts", "gemini-3.1-flash-tts-preview"];

    private async Task<bool> TryPlayNaturalGeminiFemaleVoiceAsync(string text, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;
        try
        {
            byte[]? pcmData = await FetchGeminiFemaleTtsPcmAsync(text, apiKey, ct).ConfigureAwait(false);
            if (pcmData == null || pcmData.Length == 0 || ct.IsCancellationRequested) return false;

            byte[] wavBytes = WrapPcmWithWavHeader(CleanTtsPcm(pcmData), 24000);
            using var pcmStream = new MemoryStream(wavBytes);
            using var player = new System.Media.SoundPlayer(pcmStream);
            lock (_captureLock)
            {
                if (ct.IsCancellationRequested) return false;
                _activeSoundPlayer = player;
            }

            await Task.Run(() =>
            {
                try { player.PlaySync(); } catch { }
            }, ct).ConfigureAwait(false);

            lock (_captureLock)
            {
                if (_activeSoundPlayer == player) _activeSoundPlayer = null;
            }

            return true;
        }
        catch (OperationCanceledException) { return true; }
        catch (Exception ex)
        {
            Program.Log($"Natural female voice error: {ex.Message}");
            return false;
        }
    }

    public static byte[] CleanTtsPcm(byte[] pcm, int rate = 24000)
    {
        int n = pcm.Length / 2;
        int win = rate / 50;
        if (n < win * 4) return pcm;

        var rms = new List<double>();
        var zcr = new List<double>();
        for (int w = 0; w + win <= n; w += win)
        {
            double sum = 0; int cross = 0; short prev = BitConverter.ToInt16(pcm, w * 2);
            for (int i = 0; i < win; i++)
            {
                short x = BitConverter.ToInt16(pcm, (w + i) * 2);
                sum += (double)x * x;
                if ((x >= 0) != (prev >= 0)) cross++;
                prev = x;
            }
            rms.Add(Math.Sqrt(sum / win));
            zcr.Add((double)cross / win);
        }

        const double absFloor = 200.0;
        bool Speechlike(int k) => rms[k] > absFloor && zcr[k] > 0.015 && zcr[k] < 0.25;
        int last = rms.Count - 1;
        while (last > 0 && !(Speechlike(last) && Speechlike(last - 1))) last--;
        int end = Math.Min(n, (last + 1) * win);

        int fade = Math.Min(end, rate / 5);
        var result = new byte[(end + rate / 2) * 2];
        Buffer.BlockCopy(pcm, 0, result, 0, end * 2);
        for (int i = 0; i < fade; i++)
        {
            int idx = (end - fade + i) * 2;
            short v = (short)(BitConverter.ToInt16(result, idx) * (1f - (float)i / fade));
            result[idx] = (byte)(v & 0xFF);
            result[idx + 1] = (byte)((v >> 8) & 0xFF);
        }
        return result;
    }

    private static async Task<byte[]?> FetchGeminiFemaleTtsPcmAsync(string text, string apiKey, CancellationToken ct)
    {
        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text } } } },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new { prebuiltVoiceConfig = new { voiceName = "Aoede" } } // natural female voice
                }
            }
        };
        string jsonPayload = JsonSerializer.Serialize(payload);

        await _ttsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (string model in CandidateTtsModels)
            {
                if (_modelThrottledUntil.TryGetValue(model, out var until) && DateTime.UtcNow < until) continue;

                for (int attempt = 0; attempt < 2 && !ct.IsCancellationRequested; attempt++)
                {
                    try
                    {
                        NoteKeyUse(apiKey);
                        using var req = new HttpRequestMessage(HttpMethod.Post, $"{GeminiApiBase}/models/{model}:generateContent");
                        req.Headers.Add("x-goog-api-key", apiKey);
                        req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(8000);

                        using var resp = await _httpClient.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            int code = (int)resp.StatusCode;
                            string err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            Program.Log($"Gemini female TTS {model} returned HTTP {code}: {TruncateApiError(err)}");
                            if (code == 429) { if (RotateKey(apiKey)) { apiKey = ReadGeminiApiKey()!; attempt--; continue; } _modelThrottledUntil[model] = DateTime.UtcNow.AddSeconds(45); break; }
                            if (code is 400 or 403 or 404) { _modelThrottledUntil[model] = DateTime.UtcNow.AddHours(6); break; }
                            continue;
                        }

                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) continue;
                        if (!candidates[0].TryGetProperty("content", out var content) ||
                            !content.TryGetProperty("parts", out var parts)) continue;

                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("inlineData", out var inlineData) &&
                                inlineData.TryGetProperty("data", out var dataElem) &&
                                dataElem.GetString() is { Length: > 0 } b64)
                                return Convert.FromBase64String(b64);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
                    catch (Exception ex)
                    {
                        Program.Log($"FetchGeminiFemaleTtsPcmAsync {model} error: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            _ttsGate.Release();
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_hookId); } catch { }
            _hookId = IntPtr.Zero;
        }

        try
        {
            _listeningTimeoutCts?.Cancel();
            _listeningTimeoutCts?.Dispose();
            try
            {
                _ttsCts?.Cancel();
                _ttsCts?.Dispose();
                _activeSoundPlayer?.Stop();
                _activeSoundPlayer?.Dispose();
            }
            catch { }
            try { _audioCapture?.StopRecording(); } catch { }
            try { _audioCapture?.Dispose(); } catch { }
            try { _meterCapture?.StopRecording(); } catch { }
            try { _meterCapture?.Dispose(); } catch { }
            _audioBuffer?.Dispose();
            _speechEngine?.RecognizeAsyncCancel();
            _speechEngine?.Dispose();
        }
        catch { /* cleanup */ }
    }
}
