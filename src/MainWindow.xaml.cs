// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The island is a state machine: Idle (pill) -> Media (expanded) / Volume (HUD) / Timer / Notify -> Idle.
/// Events from hardware & platform services call <see cref="Go"/>.
/// </summary>
public partial class MainWindow : Window
{
    private enum Mode { Idle, Compact, Media, Volume, Notify, Timer, Charging, PinTarget }

    // ---- services ------------------------------------------------------------------------
    private readonly SettingsService _settingsService = new();
    private readonly AudioVolumeService _audioService = new();
    private readonly FocusTimerService _timerService = new();
    private readonly MediaService _mediaService = new();
    private readonly NotificationService _notificationService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly PowerService _powerService = new();
    private readonly VoiceAssistantService _voiceService = new();

    // ---- state ---------------------------------------------------------------------------
    private Mode _mode = Mode.Idle;
    private Mode _preNotifyMode = Mode.Idle;
    private int _preNotifyView = (int)IslandLogic.ExpandedView.Clock;
    private bool _hover;                  // mouse is over the island
    private bool _playing;                // session is actively playing -> compact pill
    private bool _barsOn;                 // equalizer animation currently running
    private bool _isCharging;             // AC charger is actively plugged in
    private bool _mascotLaughing;
    private bool _mascotExpanded;
    private bool _mascotListening;
    private bool _mascotThinking;
    private bool _mascotWasSleeping;
    private bool _mascotAsleep;      // latched deep sleep (only used when MascotDeepSleep on)
    private bool _mascotStartled;    // woken by tap: fast eye pop + big hop
    private double _mascotWakeAt = -9;
    private double _mascotEyeOpen = 1;   // 0 asleep .. 1 awake, eased so waking is gentle
    private double _mascotThinkSince;
    private double _mascotMicLevel;
    private DateTime _mascotLaughUntil;
    private DateTime _mascotLastActivity = DateTime.Now;
    private DateTime _mascotNextBlink = DateTime.Now.AddSeconds(2.5);
    private DateTime _mascotBlinkStarted;
    private DateTime _mascotBlinkUntil;
    private Point _mascotLastPointer;
    private double _mascotEyeX, _mascotEyeY;
    private readonly Stopwatch _mascotClock = Stopwatch.StartNew();
    private double _mascotLastTime;
    private bool _mascotRenderingHooked;
    private const double MascotSleepAfter = 14;   // idle seconds until sleep; bored 2s..9s, sleepy 9s..14s

    // ---- timers --------------------------------------------------------------------------
    private readonly DispatcherTimer _trim = new() { Interval = TimeSpan.FromSeconds(6) };   // debounce memory trim
    private readonly DispatcherTimer _clock = new();    // ticks once per minute, aligned to :00
    private readonly DispatcherTimer _revert = new();   // "go back to idle in N s"
    private readonly DispatcherTimer _privacyTimer = new() { Interval = TimeSpan.FromSeconds(3.0) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.0) };
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    // ---- tray & settings -----------------------------------------------------------------
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private System.Drawing.Icon? _appIcon;
    private uint _wmTaskbarCreated;
    private readonly System.Windows.Forms.ToolStripMenuItem _item12;
    private readonly System.Windows.Forms.ToolStripMenuItem _item24;


    // Horizontal window 440x160: pill at x 125..315, y 6..40.
    private BackdropBlur? _backdrop;
    private readonly System.Windows.Forms.ToolStripMenuItem _itemBlur;
    private readonly System.Windows.Forms.ToolStripMenuItem _itemBlack;

    // blur on = frosted backdrop; off (or unsupported Windows) = pure black
    public void SetBackdrop(bool blur, bool save = true)
    {
        bool forced = blur && _settingsService.MascotEnabled;   // mascot = pure black, no frosted sheen
        if (forced) blur = false;
        if (blur) blur = _backdrop?.Start() == true;
        else _backdrop?.Stop();
        _itemBlur.Checked = blur;
        _itemBlack.Checked = !blur;
        if (save && !forced) _settingsService.SetBackdropBlur(blur);
    }

    private System.Windows.Media.Brush? _rim;
    /// <summary>Mascot on: no white glass rim, no blur sheen. Off: restore both.</summary>
    private void ApplyMascotLook()
    {
        _rim ??= IslandShape.Stroke;
        IslandShape.Stroke = _settingsService.MascotEnabled ? System.Windows.Media.Brushes.Transparent : _rim;
        SetBackdrop(_settingsService.UseBackdropBlur, save: false);
        SetMascotEyeColor(_settingsService.MascotEyeColor);
    }
    private bool _bottom;   // docked at bottom edge: pill anchored to window bottom, so it grows upward
    private int _rot;       // 0 = top/bottom, -90 = left edge, 90 = right edge
    private const double WinW = 440, TopGap = 6;

    private int _view;          // expanded card page: 0 clock, 1 music, 2 timer, 3 clipboard (scroll down / up)
    private long _lastWheel;
    private int _setMin = 25;   // minutes chosen on the timer page

    public MainWindow()
    {
        Program.Log("MainWindow constructor started.");
        InitializeComponent();
        Program.Log("InitializeComponent completed.");

        _clipboardService.LatestTextChanged += _ => Dispatcher.InvokeAsync(RefreshClipboardView);
        _clipboardService.HistoryChanged += _ => Dispatcher.InvokeAsync(RefreshClipboardView);

        _view = IslandLogic.GetInitialExpandedView(_playing);
        ApplyView();

        // Scale 0.9 <-> 1 while fading, so content grows out of / shrinks into the island
        foreach (var panel in new UIElement[] { IdlePanel, CompactPanel, MediaPanel, VolumePanel, NotifyPanel, TimerPanel, ChargingPanel, PinPanel })
        {
            panel.RenderTransformOrigin = new Point(0.5, 0.5);
            panel.RenderTransform = new ScaleTransform(0.9, 0.9);
        }
        IdlePanel.Opacity = 1;
        if (_settingsService.MascotEnabled) HookMascotRendering(true);
        ((ScaleTransform)IdlePanel.RenderTransform).ScaleX = ((ScaleTransform)IdlePanel.RenderTransform).ScaleY = 1;

        // Memory diet: compact the heap once quiet
        _trim.Tick += (_, _) => { _trim.Stop(); TrimMemory(); };
        _trim.Start();

        // Clock: aligned to next minute boundary
        _clock.Tick += (_, _) => UpdateClock();
        SystemEvents.TimeChanged += OnSystemTimeChanged;
        UpdateClock();

        // Privacy indicator checker
        _privacyTimer.Tick += (_, _) => CheckPrivacy();
        _privacyTimer.Start();
        CheckPrivacy();

        // Revert timer: after the hold time, drop back to idle (or stay expanded while hovered)
        _revert.Tick += (_, _) =>
        {
            _revert.Stop();
            if (_mode == Mode.Notify)
            {
                DismissNotification();
            }
            else
            {
                bool keepMedia = _hover && (!_settingsService.MascotEnabled || _mode == Mode.Media);
                Go(keepMedia ? Mode.Media : Rest());
            }
        };

        // Tray icon setup with dedicated Dynamic Island high-DPI icon
        Program.Log("Loading _appIcon...");
        _appIcon = GetAppIcon();
        Program.Log($"_appIcon loaded. Icon is null? {_appIcon == null}");
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "Dynamic Island",
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };

        _item12 = new System.Windows.Forms.ToolStripMenuItem("12-hour clock", null, (_, _) => SetClockFormat(false))
            { Checked = !_settingsService.Use24HourClock };
        _item24 = new System.Windows.Forms.ToolStripMenuItem("24-hour clock", null, (_, _) => SetClockFormat(true))
            { Checked = _settingsService.Use24HourClock };

        _itemBlur = new System.Windows.Forms.ToolStripMenuItem("Blur backdrop", null, (_, _) => SetBackdrop(true))
            { Checked = _settingsService.UseBackdropBlur };
        _itemBlack = new System.Windows.Forms.ToolStripMenuItem("Pure black", null, (_, _) => SetBackdrop(false))
            { Checked = !_settingsService.UseBackdropBlur };

        var settingsMenu = new System.Windows.Forms.ToolStripMenuItem("Settings...", null, (_, _) => OpenSettings())
        {
            Font = new System.Drawing.Font(System.Windows.Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold)
        };

        var hide = new System.Windows.Forms.ToolStripMenuItem("Hide island");
        void ToggleIsland()
        {
            Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            hide.Text = Visibility == Visibility.Visible ? "Hide island" : "Show island";
            if (Visibility == Visibility.Visible) EnforceTopmost();
        }
        hide.Click += (_, _) => ToggleIsland();
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                OpenSettings();
        };

        var alwaysOnTop = new System.Windows.Forms.ToolStripMenuItem("Always on top of every app", null,
            (s, _) =>
            {
                var item = (System.Windows.Forms.ToolStripMenuItem)s!;
                bool next = !item.Checked;
                item.Checked = next;
                _settingsService.SetAlwaysOnTopFullscreen(next);
                if (next) EnforceTopmost();
            })
            { Checked = _settingsService.AlwaysOnTopFullscreen };

        var test = new System.Windows.Forms.ToolStripMenuItem("Test notification", null,
            (_, _) => Notify("Dynamic Island", "Hello! This is how a notification looks."));

        var chargingAlwaysOn = new System.Windows.Forms.ToolStripMenuItem("Charging indicator always on", null,
            (s, _) =>
            {
                var item = (System.Windows.Forms.ToolStripMenuItem)s!;
                bool next = !item.Checked;
                item.Checked = next;
                _settingsService.SetAlwaysShowChargingIndicator(next);
                SetAlwaysCharging(next);
            })
            { Checked = _settingsService.AlwaysShowChargingIndicator };

        var focus = new System.Windows.Forms.ToolStripMenuItem("Focus timer");
        foreach (var m in new[] { 5, 10, 15, 25, 45, 60 })
            focus.DropDownItems.Add($"{m} min", null, (_, _) => StartTimer(m));
        focus.DropDownItems.Add("Custom...", null, (_, _) => { if (AskMinutes() is int m) StartTimer(m); });
        focus.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        focus.DropDownItems.Add("Cancel timer", null, (_, _) => CancelTimer());

        var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = _settingsService.IsStartWithWindowsEnabled()
        };
        startup.Click += (_, _) =>
        {
            bool willEnable = !startup.Checked;
            bool ok = _settingsService.SetStartWithWindows(willEnable);
            startup.Checked = _settingsService.IsStartWithWindowsEnabled();
            if (willEnable && !ok)
            {
                Notify("Dynamic Island", "Could not enable Start with Windows.");
            }
        };

        var pttMenu = new System.Windows.Forms.ToolStripMenuItem("Push-to-Talk Shortcut");
        var pttOptions = new (string Label, int Vk)[]
        {
            ("Right Ctrl", 0xA3),
            ("Left Ctrl", 0xA2),
            ("Ctrl (Any)", 0x11),
            ("Right Alt", 0xA5),
            ("Left Alt", 0xA4),
            ("Caps Lock", 0x14),
            ("Space", 0x20),
            ("F8", 0x77),
            ("F9", 0x78),
            ("F10", 0x79),
            ("F11", 0x7A),
            ("F12", 0x7B),
            ("Disabled", 0x00),
        };

        var pttMenuItems = new System.Collections.Generic.List<(System.Windows.Forms.ToolStripMenuItem Item, int Vk)>();
        foreach (var (label, vk) in pttOptions)
        {
            int currentVk = vk;
            var item = new System.Windows.Forms.ToolStripMenuItem(label, null, (_, _) =>
            {
                string shortcutName = $"[{label}]";
                _settingsService.SetPushToTalkShortcut(shortcutName, currentVk, 0); // clears stale combo modifier too
                _voiceService.SetPushToTalkShortcut(shortcutName, currentVk, 0);
                foreach (var (subItem, itemVk) in pttMenuItems)
                {
                    subItem.Checked = (itemVk == currentVk);
                }
            })
            {
                Checked = (_settingsService.PushToTalkVirtualKey == currentVk)
            };
            pttMenuItems.Add((item, currentVk));
            pttMenu.DropDownItems.Add(item);
        }

        var wakeWordItem = new System.Windows.Forms.ToolStripMenuItem("Wake-Word Listening (\"Hey Nixi\")", null,
            (s, _) =>
            {
                var item = (System.Windows.Forms.ToolStripMenuItem)s!;
                bool next = !item.Checked;
                item.Checked = next;
                _settingsService.SetWakeWordEnabled(next);
                _voiceService.SetWakeWordEnabled(next);
            })
            { Checked = _settingsService.WakeWordEnabled };

        _tray.ContextMenuStrip.Opening += (_, _) =>
        {
            startup.Checked = _settingsService.IsStartWithWindowsEnabled();
            alwaysOnTop.Checked = _settingsService.AlwaysOnTopFullscreen;
            wakeWordItem.Checked = _settingsService.WakeWordEnabled;
            foreach (var (subItem, itemVk) in pttMenuItems)
            {
                subItem.Checked = (itemVk == _settingsService.PushToTalkVirtualKey);
            }
        };

        _tray.ContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[]
        {
            settingsMenu,
            new System.Windows.Forms.ToolStripSeparator(),
            hide,
            alwaysOnTop,
            focus,
            test,
            chargingAlwaysOn,
            pttMenu,
            wakeWordItem,
            _item12,
            _item24,
            new System.Windows.Forms.ToolStripSeparator(),
            _itemBlur,
            _itemBlack,
            new System.Windows.Forms.ToolStripSeparator(),
            startup,
            new System.Windows.Forms.ToolStripSeparator(),
            new System.Windows.Forms.ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown()),
        });
        Program.Log("Setting _tray.Visible = true...");
        _tray.Visible = true;
        Program.Log($"_tray.Visible set. Current value: {_tray.Visible}");

        Root.MouseLeftButtonDown += (_, e) =>
        {
            if (_settingsService.MascotEnabled && _mascotAsleep && _settingsService.MascotDeepSleep)
            {
                _mascotAsleep = false; _mascotStartled = true; _mascotLastActivity = DateTime.Now;   // tap wakes it
                e.Handled = true;
                return;
            }
            if (_settingsService.MascotEnabled && e.ClickCount >= 3)
            {
                _mascotExpanded = true;
                _mascotLaughing = false;
                _view = IslandLogic.GetInitialExpandedView(_playing);
                ApplyView();
                Go(Mode.Media);   // sets _mode so OnIslandLeave can collapse it
                e.Handled = true;
                return;
            }
            if (_settingsService.MascotEnabled && _playing && _mode != Mode.Media && e.ClickCount == 1)
            {
                _view = (int)IslandLogic.ExpandedView.Music;
                ApplyView();
                Go(Mode.Media, 4.5);
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2)
            {
                if (_settingsService.MascotEnabled)
                {
                    _mascotLaughing = true;
                    _mascotLaughUntil = DateTime.Now.AddSeconds(1.1);
                    _mascotLastActivity = DateTime.Now;
                    e.Handled = true;
                }
                else ResetPosition();
                return;
            }
            if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;

            if (_mode == Mode.Notify)
            {
                DismissNotification();
                return;
            }

            // Only allow dragging when clicking and holding on the center of the island.
            // Measure in Root's local (unrotated) space so the hit zone still matches after the
            // island is rotated onto the left/right edge (window coords would no longer line up).
            System.Windows.Point pt = e.GetPosition(Root);
            if (!IslandLogic.IsCenterDragZone(pt.X, pt.Y, ShapeGeo.Rect, _mode.ToString()))
            {
                return;
            }

            double l = Left, t = Top;
            DragMove();
            if (Left != l || Top != t)
            {
                _userMoved = true;
                SnapToEdge();
                // DragMove() can drop HWND_TOPMOST against full-screen apps; re-assert immediately.
                EnforceTopmost();
            }
        };

        // right-click = back to top-center (double-click is the laugh when mascot on)
        Root.MouseRightButtonDown += (_, e) => { ResetPosition(); e.Handled = true; };
        Root.MouseRightButtonUp += (_, e) => e.Handled = true;

        PreviewMouseWheel += (_, e) =>
        {
            if (_mode != Mode.Media) return;
            e.Handled = true;
            if (Environment.TickCount64 - _lastWheel < 250) return;
            int v = IslandLogic.NextView(_view, e.Delta);
            if (v == _view) return;
            _lastWheel = Environment.TickCount64;
            int dir = Math.Sign(v - _view);
            _view = v;
            ApplyView();
            RevealView(dir, 0);
        };

        SourceInitialized += (_, _) => MakeOverlay();

        Loaded += async (_, _) =>
        {
            Program.Log("MainWindow Loaded event fired.");
            EnsureTrayIconVisible();
            SetIslandScale(_settingsService.IslandScale);
            _setMin = _settingsService.FocusTimerDefaultMinutes;
            Position();
            InitTopmostEnforcer();
            _backdrop = new BackdropBlur(this, IslandShape, IslandTint);
            ApplyMascotLook();
            _settingsService.EnsureStartupPathUpToDate();
            InitVoiceAssistant();
            InitVolume();
            InitFocusTimer();
            InitPower();
            await InitMediaAsync();
            await InitNotificationEngineAsync();

            _toastTimer.Tick += async (_, _) =>
            {
                try
                {
                    await PollNativeNotificationsAsync();
                    PollToast();
                }
                catch (Exception ex) { Program.Log(ex); }
            };
            _toastTimer.Start();

            _watchTimer.Tick += (_, _) => Watchdog();
            _watchTimer.Start();

            // Perform post-startup working set trim once initial templates, fonts, and icons settle
            var startupTrim = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(2.0)
            };
            startupTrim.Tick += (_, _) =>
            {
                startupTrim.Stop();
                TrimMemory();
            };
            startupTrim.Start();

            // Pre-warm SettingsWindow in background at idle so opening it from tray is INSTANT (0ms)
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_settingsWindow == null)
                    {
                        _settingsWindow = new SettingsWindow(_settingsService, this, _voiceService);
                        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                        _settingsWindow.PreWarm();
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"Pre-warm SettingsWindow: {ex}");
                }
            }, DispatcherPriority.ApplicationIdle);

            // Prompt for Gemini API key on first run if missing and model provider is cloud
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(800);
                CheckApiKeySetupOnFirstRun();
            }, DispatcherPriority.ApplicationIdle);
        };

        Closed += (_, _) =>
        {
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            ShutdownTopmostEnforcer();

            _clock.Stop();
            _trim.Stop();
            _privacyTimer.Stop();
            _revert.Stop();
            _toastTimer.Stop();
            _watchTimer.Stop();
            HookMascotRendering(false);

            try
            {
                _tray.Visible = false;
                _tray.Dispose();
                _appIcon?.Dispose();
            }
            catch { /* shutdown cleanup */ }

            _audioService.Dispose();
            _timerService.Dispose();
            _powerService.Dispose();
            _mediaService.Dispose();
            _notificationService.Dispose();
            _clipboardService.Dispose();
            _voiceService.Dispose();

            try
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.IsShuttingDown = true;
                    _settingsWindow.Close();
                }
            }
            catch { }
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateClock);
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => Position(false));

    private void InitVoiceAssistant()
    {
        _voiceService.SetVolumeAction = vol => Dispatcher.InvokeAsync(() => _audioService.Volume = vol);
        _voiceService.SkipTrackAction = next => Dispatcher.Invoke(() => next ? _mediaService.Next() : _mediaService.Previous());
        _voiceService.GetVolumeFunc = () => _audioService.Volume;

        _voiceService.ListeningStateChanged += (isListening, reason) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _mascotListening = isListening;
                if (!isListening) _mascotMicLevel = 0;
                bool mascot = _settingsService.MascotEnabled;   // mascot on: no pill, mascot animates instead
                _mascotThinking = mascot && !isListening && (reason == "Processing" || reason == "Thinking");
                _mascotThinkSince = _mascotClock.Elapsed.TotalSeconds;
                if (isListening)
                {
                    if (!mascot) Notify("Hey Nixi", "Listening... Speak your command", "VoiceAssistant", 7.0);
                }
                else
                {
                    if (reason == "Processing" || reason == "Thinking")
                    {
                        if (!mascot) Notify("Hey Nixi", "Thinking...", "VoiceAssistant", 6.0);
                    }
                    else if (reason == "Cancel")
                    {
                        if (_mode == Mode.Notify)
                        {
                            DismissNotification();
                        }
                        SyncRest();
                    }
                }
            });
        };

        _voiceService.AudioLevelChanged += level => Dispatcher.InvokeAsync(() => _mascotMicLevel = level);

        _voiceService.CommandRecognized += command =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_settingsService.MascotEnabled) Notify("Hey Nixi", $"\"{command}\"", "VoiceAssistant", 3.5);
            });
        };

        _voiceService.ActionExecuted += (actionSummary, replyVoice) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _mascotThinking = false;
                Notify("Hey Nixi", actionSummary, "VoiceAssistant", 4.5);
   // no forced GC here: 2 blocking compacting GCs froze the UI right after every command
            });
        };

        // Restore ALL saved voice settings (previously only key + wake word were applied after restart)
        _voiceService.SetPushToTalkShortcut(_settingsService.PushToTalkShortcutName, _settingsService.PushToTalkVirtualKey, _settingsService.PushToTalkModifierKey);
        _voiceService.SetPushToTalkEnabled(_settingsService.PushToTalkEnabled);
        _voiceService.SetWakePhrase(_settingsService.WakePhrase);
        _voiceService.SetMicrophoneDevice(_settingsService.MicrophoneDeviceId);
        _voiceService.SetMicrophoneSensitivity(_settingsService.MicrophoneSensitivity);
        _voiceService.SetWakeWordEnabled(_settingsService.WakeWordEnabled);
        _voiceService.SetModelProvider(_settingsService.ModelProvider);
        _voiceService.SetLocalModel(_settingsService.LocalModelName);
        _voiceService.SetLocalEndpoint(_settingsService.LocalModelEndpoint);
        if (_settingsService.ModelProvider == "local")
            _ = Task.Run(() => _voiceService.EnsureLocalModelReadyAsync()); // start server / pull model at startup
        _voiceService.Initialize();
    }


    private void MakeOverlay()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = new IntPtr(exStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, newStyle);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        _clipboardService.Initialize(hwnd);

        // Listen for TaskbarCreated message so tray icon always persists when Explorer restarts/refreshes
        _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProcTrayHook);
    }

    private IntPtr WndProcTrayHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_wmTaskbarCreated != 0 && (uint)msg == _wmTaskbarCreated)
        {
            Dispatcher.InvokeAsync(RecreateTrayIcon);
        }
        else if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            HandleWindowPosChanging(lParam);
        }
        else if (msg == NativeMethods.WM_EXITSIZEMOVE)
        {
            // DragMove()'s internal loop can strip WS_EX_TOPMOST against full-screen apps.
            // Re-assert immediately the moment the OS signals the drag is done.
            Dispatcher.InvokeAsync(EnforceTopmost, DispatcherPriority.Normal);
        }
        return IntPtr.Zero;
    }

    private void EnsureTrayIconVisible()
    {
        try
        {
            if (_tray == null) return;
            if (_appIcon == null) _appIcon = GetAppIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "Dynamic Island";
            if (!_tray.Visible)
            {
                Program.Log("Setting _tray.Visible = true in EnsureTrayIconVisible");
                _tray.Visible = true;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"EnsureTrayIconVisible error: {ex.Message}");
        }
    }

    private void RecreateTrayIcon()
    {
        try
        {
            if (_tray != null)
            {
                if (_appIcon == null) _appIcon = GetAppIcon();
                _tray.Visible = false;
                _tray.Icon = _appIcon;
                _tray.Text = "Dynamic Island";
                _tray.Visible = true;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"Tray recreate error: {ex.Message}");
        }
    }

    private static System.Drawing.Icon GetAppIcon()
    {
        try
        {
            // 1. Try file from assets directory (highest fidelity multi-size DIB)
            var assetIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app.ico");
            if (File.Exists(assetIco))
            {
                return new System.Drawing.Icon(assetIco);
            }

            // 2. Try embedded manifest resource
            var asm = typeof(MainWindow).Assembly;
            using var stream = asm.GetManifestResourceStream("DynamicIsland.assets.app.ico");
            if (stream != null)
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return new System.Drawing.Icon(ms);
            }

            // 3. Try associated icon of the running executable
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to load app icon file: {ex.Message}");
        }

        return CreateIslandTrayIcon();
    }

    private static System.Drawing.Icon CreateIslandTrayIcon()
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(System.Drawing.Color.Transparent);

                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                int x = 3, y = 9, w = 26, r = 7;
                path.AddArc(x, y, r * 2, r * 2, 90, 180);
                path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 180);
                path.CloseFigure();

                using var fillBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 12, 13, 18));
                g.FillPath(fillBrush, path);

                using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 255, 255, 255), 1.4f);
                g.DrawPath(pen, path);

                using var lensBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 70, 80, 100));
                g.FillEllipse(lensBrush, 7, 13, 5, 5);

                using var amberBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 165, 20));
                g.FillEllipse(amberBrush, 19, 13, 5, 5);
            }

            var hIcon = bmp.GetHicon();
            var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
            NativeMethods.DestroyIcon(hIcon);
            return icon;
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private SettingsWindow? _settingsWindow;

    public void OpenSettings()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = new SettingsWindow(_settingsService, this, _voiceService);
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                }
                _settingsWindow.Show();
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Topmost = true;
                _settingsWindow.Activate();
                _settingsWindow.Focus();
            }
            catch (Exception ex)
            {
                Program.Log($"OpenSettings error: {ex}");
            }
        });
    }

    private void CheckApiKeySetupOnFirstRun()
    {
        try
        {
            if (_settingsService.ModelProvider == "cloud" && !VoiceAssistantService.HasValidApiKey())
            {
                OpenApiKeySetup();
            }
        }
        catch (Exception ex)
        {
            Program.Log($"CheckApiKeySetupOnFirstRun error: {ex.Message}");
        }
    }

    public void OpenApiKeySetup()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var win = new ApiKeyWindow(key =>
                {
                    Notify("Hey Nixi", "Gemini AI key configured successfully!", "VoiceAssistant", 4.0);
                });
                win.Show();
                win.Activate();
            }
            catch (Exception ex)
            {
                Program.Log($"OpenApiKeySetup error: {ex.Message}");
            }
        });
    }

    private double _islandScale = 1.0;

    public void SetMascotEnabled(bool enabled)
    {
        if (_settingsService.MascotEnabled != enabled) _settingsService.SetMascotEnabled(enabled);
        _mascotLaughing = false; _mascotExpanded = false; _mascotAsleep = false; _mascotLastActivity = DateTime.Now;
        if (enabled) HookMascotRendering(true); else { HookMascotRendering(false); MascotSleepZ.Opacity = 0; MascotListenWaves.Visibility = Visibility.Collapsed; MascotListenGlow.Visibility = MascotListenRipple.Visibility = MascotListenRainbow.Visibility = Visibility.Collapsed; }
        ApplyMascotLook();
        Animate(_mode);
        CheckPrivacy();
    }

    public void SetMascotNeverSleep(bool on)
    {
        if (_settingsService.MascotNeverSleep != on) _settingsService.SetMascotNeverSleep(on);
        if (on)
        {
            _mascotAsleep = false;
        }
    }

    public void SetMascotPrivacyIndicatorsEnabled(bool enabled)
    {
        if (_settingsService.MascotPrivacyIndicatorsEnabled != enabled)
            _settingsService.SetMascotPrivacyIndicatorsEnabled(enabled);
        CheckPrivacy();
    }

    public void SetMascotEyeColor(string hexColor)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetMascotEyeColor(hexColor));
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(hexColor)) hexColor = "#829BFF";
            var brush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hexColor)!;
            var color = brush.Color;
            if (MascotEyeLeftShape != null) MascotEyeLeftShape.Background = brush;
            if (MascotEyeRightShape != null) MascotEyeRightShape.Background = brush;
            if (MascotEyeLeftGlow != null) MascotEyeLeftGlow.Color = color;
            if (MascotEyeRightGlow != null) MascotEyeRightGlow.Color = color;
            if (MascotSmileLeftShape != null) MascotSmileLeftShape.Stroke = brush;
            if (MascotSmileRightShape != null) MascotSmileRightShape.Stroke = brush;
            if (MascotSmileLeftGlow != null) MascotSmileLeftGlow.Color = color;
            if (MascotSmileRightGlow != null) MascotSmileRightGlow.Color = color;
        }
        catch (Exception ex)
        {
            Program.Log($"SetMascotEyeColor error: {ex.Message}");
        }
    }


    private void HookMascotRendering(bool hook)
    {
        if (hook == _mascotRenderingHooked) return;
        _mascotRenderingHooked = hook;
        if (hook)
        {
            _mascotLastTime = _mascotClock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnMascotRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnMascotRendering;
        }
    }

    private void OnMascotRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        try { UpdateMascot(); } catch (Exception ex) { Program.Log(ex); }
    }

    private void UpdateMascot()
    {
        if (!_settingsService.MascotEnabled) return;
        if (!IsLoaded || !NativeMethods.GetCursorPos(out NativeMethods.POINT cursor)) return;

        double nowSec = _mascotClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(nowSec - _mascotLastTime, 0.001, 0.05);
        _mascotLastTime = nowSec;

        if (PresentationSource.FromVisual(this) == null) return;   // hidden/closing: PointFromScreen throws
        var p = PointFromScreen(new Point(cursor.X, cursor.Y));
        bool deep = _settingsService.MascotDeepSleep;
        bool neverSleep = _settingsService.MascotNeverSleep;
        if ((p - _mascotLastPointer).Length > 1) { _mascotLastPointer = p; if (!(deep && _mascotAsleep)) _mascotLastActivity = DateTime.Now; }   // asleep: mouse does not wake
        if (_mascotListening || _mascotThinking) _mascotLastActivity = DateTime.Now;   // talking = awake
        double idleSeconds = (DateTime.Now - _mascotLastActivity).TotalSeconds;
        if (deep)
        {
            if (_playing || _mascotListening || _mascotThinking || neverSleep) { _mascotAsleep = false; }
            else if (idleSeconds > MascotSleepAfter) _mascotAsleep = true;
        }
        bool sleeping = !neverSleep && (deep ? _mascotAsleep : !_playing && idleSeconds > MascotSleepAfter);
        double tired = (_playing || neverSleep) ? 0 : Math.Clamp((idleSeconds - (MascotSleepAfter - 5)) / 5, 0, 1);   // last 5s before sleep: sleepy
        // bored: 2s idle, fades out as sleepiness starts. 7s loop: look left, look right, eye roll, yawn.
        double bored = _playing || _mascotListening || _mascotThinking || _mascotLaughing || sleeping ? 0
            : Math.Clamp(idleSeconds - 1, 0, 0.5) * 2 * (1 - tired);
        double boredX = 0, boredY = 0, yawn = 0;
        if (bored > 0)
        {
            double bt = (idleSeconds - 1) % 7;
            if (bt < 1.6) boredX = -7;
            else if (bt < 3.2) boredX = 7;
            else if (bt < 4.8) { double a = (bt - 3.2) / 1.6 * Math.PI * 2; boredX = 6 * Math.Sin(a); boredY = -4 * Math.Cos(a); }
            else if (bt > 5) yawn = Math.Sin(Math.PI * Math.Min(1, (bt - 5) / 2));
        }
        // ease eyes open slowly (~0.35s), close slower (~0.6s): no sudden snap awake
        _mascotEyeOpen += ((sleeping ? 0 : 1) - _mascotEyeOpen) * (1 - Math.Exp(-dt / (sleeping ? 0.6 : _mascotStartled ? 0.06 : 0.35)));
        double open = _mascotEyeOpen;
        if (_mascotWasSleeping && !sleeping) _mascotWakeAt = nowSec;   // just woke: trigger hop + eye pop
        _mascotWasSleeping = sleeping;
        double wakeT = nowSec - _mascotWakeAt;
        if (wakeT > 1) _mascotStartled = false;
        double wakePop = wakeT < 1 ? Math.Exp(-wakeT * 5) * (_mascotStartled ? 2.2 : 1) : 0;   // startle = bigger pop + hop
        DateTime now = DateTime.Now;
        if (!sleeping && !_mascotLaughing && now >= _mascotNextBlink)
        {
            _mascotBlinkStarted = now;
            _mascotBlinkUntil = now.AddMilliseconds(180);
            _mascotNextBlink = now.AddSeconds(3 + Random.Shared.NextDouble() * 2.5);
        }
        double blinkProgress = 1 - Math.Clamp((_mascotBlinkUntil - now).TotalMilliseconds / 180, 0, 1);
        double blinkClosure = !_mascotLaughing && !sleeping && now < _mascotBlinkUntil
            ? 1 - Math.Abs(2 * blinkProgress - 1) : 0;
        MascotSleepZ.Opacity = _mascotThinking ? 1 : 1 - open;
        double breath = 1 + (sleeping ? 0.018 * Math.Sin(nowSec * 1.2) : 0.025 * Math.Sin(nowSec * 2.4)) + 0.04 * yawn * bored;
        if (Math.Abs(IslandBreathScale.ScaleX - breath) > 0.0006)
        {
            IslandBreathScale.ScaleX = IslandBreathScale.ScaleY = breath;
            IslandTintBreathScale.ScaleX = IslandTintBreathScale.ScaleY = breath;
        }
        if (MascotSleepZ.Opacity > 0.01)
        {
            double sleepAge = _mascotThinking ? (nowSec - _mascotThinkSince) : Math.Max(0, idleSeconds - MascotSleepAfter);
            string zt = _mascotThinking ? "?" : "z";
            if (MascotSleepZ1.Text != zt)
            {
                MascotSleepZ1.Text = MascotSleepZ2.Text = zt;
                MascotSleepZ3.Text = _mascotThinking ? "?" : "Z";
            }
            SetSleepZ(MascotSleepZ1Move, MascotSleepZ1, sleepAge, 0);
            SetSleepZ(MascotSleepZ2Move, MascotSleepZ2, sleepAge, 0.7);
            SetSleepZ(MascotSleepZ3Move, MascotSleepZ3, sleepAge, 1.4);
        }
        if (_mascotThinking && nowSec - _mascotThinkSince > 20) _mascotThinking = false;   // safety: never stuck
        double targetX = sleeping || _mascotLaughing ? 0 : Math.Clamp((p.X - WinW / 2) / 35, -7, 7);
        double targetY = sleeping || _mascotLaughing ? 0 : Math.Clamp((p.Y - 46) / 35, -5, 5);
        if (_mascotThinking) { targetX = 6 * Math.Cos(nowSec * 4); targetY = 4 * Math.Sin(nowSec * 4); }   // eyes orbit
        if (bored > 0) { targetX += (boredX - targetX) * bored; targetY += (boredY - targetY) * bored; }
        double smooth = 1 - Math.Exp(-dt / (0.045 + 0.4 * (1 - open) + 0.2 * bored));   // eyes drift to cursor slowly while waking
        _mascotEyeX += (targetX - _mascotEyeX) * smooth;
        _mascotEyeY += (targetY - _mascotEyeY) * smooth;
        double listenPhase = nowSec * 8;
        double listenPulse = _mascotListening ? (Math.Sin(listenPhase) + 1) / 2 : 0;
        double micLevel = Math.Clamp(_mascotMicLevel * 12, 0, 1);
        string ls = _settingsService.MascotListenStyle;
        bool glowStyle = ls != "waves";
        MascotListenWaves.Visibility = _mascotListening && !glowStyle ? Visibility.Visible : Visibility.Collapsed;
        MascotListenGlow.Visibility = _mascotListening && ls == "glow" ? Visibility.Visible : Visibility.Collapsed;
        MascotListenRipple.Visibility = _mascotListening && ls == "ripple" ? Visibility.Visible : Visibility.Collapsed;
        MascotListenRainbow.Visibility = _mascotListening && ls == "rainbow" ? Visibility.Visible : Visibility.Collapsed;
        if (_mascotListening && ls == "ripple")
            for (int i = 0; i < 2; i++)
            {
                double ph = (nowSec * (0.8 + micLevel * 0.6) + i * 0.5) % 1;   // louder = faster ripples
                var sc = i == 0 ? MascotRipple1Scale : MascotRipple2Scale;
                sc.ScaleX = 1 + 0.35 * ph; sc.ScaleY = 1 + 0.2 * ph;
                (i == 0 ? MascotRipple1 : MascotRipple2).Opacity = (1 - ph) * 0.85;
            }
        if (_mascotListening && ls == "rainbow")
        {
            MascotRainbowRot.Angle = nowSec * 140 % 360;
            MascotRainbowRing.Opacity = 0.7 + listenPulse * 0.3;
        }
        if (_mascotListening && ls == "glow")
        {
            MascotListenGlowScale.ScaleX = MascotListenGlowScale.ScaleY = breath;   // follow pill breathing
            MascotGlowRing.Opacity = 0.45 + listenPulse * 0.35 + micLevel * 0.2;     // pulse + louder = brighter
            MascotGlowComet.StrokeDashOffset = -nowSec * 9;                          // comets circle the edge
        }
        if (_mascotListening && !glowStyle) UpdateMascotListeningWaves(now, micLevel, listenPulse);
        if (_mascotThinking) listenPulse = (Math.Sin(nowSec * 6) + 1) / 2;
        if (_mascotListening || _mascotThinking)
        {
            MascotEyeLeftGlow.BlurRadius = MascotEyeRightGlow.BlurRadius = 10 + listenPulse * 7;
            MascotEyeLeftGlow.Opacity = MascotEyeRightGlow.Opacity = 0.9 + listenPulse * 0.1;
        }
        else if (MascotEyeLeftGlow.BlurRadius != 10)
        {
            MascotEyeLeftGlow.BlurRadius = MascotEyeRightGlow.BlurRadius = 10;
            MascotEyeLeftGlow.Opacity = MascotEyeRightGlow.Opacity = 0.9;
        }
        bool musicMascot = _playing && !_mascotExpanded;
        MascotShades.Visibility = !_mascotExpanded && _settingsService.MascotAccessory == "shades" ? Visibility.Visible : Visibility.Collapsed;
        MascotBow.Visibility = !_mascotExpanded && _settingsService.MascotAccessory == "bow" ? Visibility.Visible : Visibility.Collapsed;
        MascotHeadphones.Visibility = musicMascot ? Visibility.Visible : Visibility.Collapsed;
        double musicBob = musicMascot ? Math.Sin(nowSec * 4.5) * 1.2 : 0;
        musicBob -= wakePop * Math.Abs(Math.Sin(wakeT * 11)) * 3;                 // wake hop
        if (_mascotListening) musicBob -= micLevel * 2.5 + Math.Abs(Math.Sin(nowSec * 8)) * 0.8;   // bounce to voice
        if (_mascotThinking) musicBob += Math.Sin(nowSec * 6) * 0.8;               // thoughtful sway
        musicBob += 1.5 * tired + 2 * bored * Math.Sin(nowSec * 1.5) + 3 * yawn * bored;   // head sags
        double eyeH = 12 * open * (1 - blinkClosure) * (1 - 0.8 * yawn * bored) * (1 + 0.35 * wakePop + (_mascotListening ? 0.25 * micLevel : 0) - (_mascotThinking ? 0.15 : 0));
        double laughPhase = (_mascotLaughUntil - DateTime.Now).TotalSeconds;
        double giggle = _mascotLaughing ? Math.Sin((1.1 - laughPhase) * 22) : 0;
        double giggleY = _mascotLaughing ? Math.Sin((1.1 - laughPhase) * 10.5) * 2.5 : 0;
        MascotHeadphonesBob.X = _mascotEyeX;
        MascotHeadphonesBob.Y = _mascotEyeY + giggleY + musicBob;
        MascotShadesMove.X = _mascotEyeX;
        MascotShadesMove.Y = _mascotEyeY + giggleY + musicBob;
        MascotBowMove.X = _mascotEyeX;
        MascotBowMove.Y = _mascotEyeY + giggleY + musicBob;
        MascotEyeLeft.X = MascotEyeRight.X = _mascotEyeX;
        MascotEyeLeft.Y = MascotEyeRight.Y = _mascotEyeY + giggleY + musicBob;
        double droop = 1 - 0.3 * tired;                                            // heavy lids
        double wink = tired * Math.Pow(Math.Max(0, Math.Sin(nowSec * 0.9)), 6);   // slow one-eye close, ~7s cycle
        MascotEyeLeftShape.Height = Math.Min(19, 3 + eyeH * droop * (1 - 0.85 * wink));
        MascotEyeRightShape.Height = Math.Min(19, 3 + eyeH * droop);
        MascotEyeLeftShape.Opacity = MascotEyeRightShape.Opacity = _mascotLaughing ? 0 : 1;
        MascotSmileEyes.Visibility = _mascotLaughing ? Visibility.Visible : Visibility.Collapsed;
        MascotSmileLeft.X = MascotSmileRight.X = _mascotEyeX;
        MascotSmileLeft.Y = MascotSmileRight.Y = _mascotEyeY + giggleY + musicBob;
        MascotSmileEyes.Opacity = _mascotLaughing ? 0.88 + Math.Abs(giggle) * 0.12 : 1;
        if (_mascotLaughing && DateTime.Now >= _mascotLaughUntil) _mascotLaughing = false;
    }

    private void UpdateMascotListeningWaves(DateTime now, double micLevel, double pulse)
    {
        double time = now.TimeOfDay.TotalSeconds;
        for (int i = 0; i < 2; i++)
        {
            double phase = (time * 0.9 + i * 0.5) % 1;
            double radius = 5 + phase * 9 + micLevel * 2;
            double opacity = (1 - phase) * (0.35 + pulse * 0.65);
            SetMascotListeningArc(i == 0 ? MascotListenWaveLeftInner : MascotListenWaveLeftOuter, 26, radius, true, opacity);
            SetMascotListeningArc(i == 0 ? MascotListenWaveRightInner : MascotListenWaveRightOuter, 128, radius, false, opacity);
        }
    }

    private static void SetMascotListeningArc(System.Windows.Shapes.Path path, double centerX, double radius, bool left, double opacity)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(centerX, 26.5 - radius), false, false);
            context.ArcTo(new Point(centerX, 26.5 + radius), new Size(radius, radius), 0, false,
                left ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        path.Data = geometry;
        path.Opacity = opacity;
    }
    private static void SetSleepZ(TranslateTransform move, System.Windows.Controls.TextBlock z, double age, double offset)
    {
        double cycle = (age + offset) % 1.8 / 1.8;
        move.Y = -10 * cycle;
        move.X = Math.Sin(cycle * Math.PI * 2) * 2;
        z.Opacity = 1 - cycle;
    }

    public void SetIslandScale(double scale)
    {
        if (scale is < 0.5 or > 2.0) return;
        _islandScale = scale;
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        Position();
    }

    public void SetIslandPosition(string position)
    {

        _userMoved = false;
        Position();
    }

    public void SetFocusMinutes(int min)
    {
        _setMin = min;
        if (!_timerService.IsActive) RefreshTimerView();
    }

    private bool _userMoved;   // user dragged island: only explicit reset/settings may recenter

    private void Position(bool force = true)
    {
        if (!force && _userMoved) return;
        double effectiveWidth = Width * _islandScale;
        string pos = _settingsService.IslandPosition;
        if (pos == "left")
        {
            Left = 20;
        }
        else if (pos == "right")
        {
            Left = Math.Max(0, SystemParameters.PrimaryScreenWidth - effectiveWidth - 20);
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - effectiveWidth) / 2;
        }
        Top = 0;
        EnforceTopmost();
    }

    private void Go(Mode mode, double holdSeconds = 0)
    {
        _revert.Stop();
        EnforceTopmost();
        var prev = _mode;
        bool timerNeedsReanim = mode == Mode.Timer &&
            IslandLogic.ShouldReanimateTimerPill(ShapeGeo.Rect.Width, _playing, _privacyActive);

        if (mode != _mode || timerNeedsReanim)
        {
            _mode = mode;
            if (mode != Mode.Media && mode != Mode.Notify)
            {
                _view = IslandLogic.GetInitialExpandedView(_playing);
                ApplyView();
            }
            Animate(mode);
            if (mode == Mode.Media && prev != Mode.Media) RevealView(0, 0.18);
        }
        if (holdSeconds > 0)
        {
            _revert.Interval = TimeSpan.FromSeconds(holdSeconds);
            _revert.Start();
        }

        _trim.Stop();
        _trim.Start();
    }

    private static void TrimMemory()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            NativeMethods.TrimWorkingSet();
        }
        catch { }
    }

    private void Animate(Mode mode)
    {
        var (w, h, r) = IslandLogic.GetPillDimensions(mode.ToString(), _playing, _privacyActive, _isCharging);
        if (_settingsService.MascotEnabled && !_mascotExpanded && mode != Mode.Media && mode != Mode.Notify) { w = 102; h = 53; r = 26.5; }

        if (mode == Mode.Idle)
        {
            IdlePanel.Width = w;
        }

        if (mode == Mode.Timer)
        {
            TimerMusicBars.Visibility = _playing ? Visibility.Visible : Visibility.Collapsed;
        }

        _backdrop?.Refresh();   // re-sample what is behind the island as it moves/morphs
        var cur = ShapeGeo.Rect;
        double curR = ShapeGeo.RadiusX;
        bool grow = w * h > cur.Width * cur.Height;
        double amp = mode == Mode.Media ? 0.7 : mode == Mode.Notify ? 0.9 : mode == Mode.Charging ? 0.55 : 0.45;
        // grow = back.out overshoot (island "inflates"); shrink = power4.out settle
        var ease = grow ? Ease.BackOut(1 + amp * 1.5) : Ease.PowerOut(4);
        double dur = grow ? (mode == Mode.Media ? 0.62 : mode == Mode.Notify ? 0.66 : 0.5) : 0.42;

        var to = (_settingsService.MascotEnabled && !_mascotExpanded && mode != Mode.Media && mode != Mode.Notify) ? new Rect((WinW-w)/2, TopGap, w, h) : IslandLogic.CalculateIslandRect(WinW, w, h, _bottom, TopGap);
        double dotX = (WinW + w) / 2 - 28;
        double dotY = _bottom ? 160 - TopGap - h + (h - 6) / 2 : TopGap + (h - 6) / 2;
        double px = PrivacyShift.X, py = PrivacyShift.Y;

        // one tween drives shape, clip, corner radius and privacy dot together
        Gsap.To(ShapeGeo, "morph", dur, ease, p =>
        {
            var rc = new Rect(
                cur.X + (to.X - cur.X) * p, cur.Y + (to.Y - cur.Y) * p,
                Math.Max(0, cur.Width + (to.Width - cur.Width) * p), Math.Max(0, cur.Height + (to.Height - cur.Height) * p));
            double rad = Math.Max(0, curR + (r - curR) * p);
            foreach (var g in new[] { ShapeGeo, ClipGeo })
            {
                g.Rect = rc;
                g.RadiusX = g.RadiusY = rad;
            }
            PrivacyShift.X = px + (dotX - px) * p;
            PrivacyShift.Y = py + (dotY - py) * p;
        });

        bool showPanels = !_settingsService.MascotEnabled || _mascotExpanded;
        Fade(IdlePanel,     showPanels && mode == Mode.Idle);
        Fade(CompactPanel,  showPanels && mode == Mode.Compact);
        Fade(MediaPanel,    mode == Mode.Media);
        Fade(VolumePanel,   showPanels && mode == Mode.Volume);
        Fade(NotifyPanel,   mode == Mode.Notify);   // always show: mascot has no notify face
        Fade(TimerPanel,    showPanels && mode == Mode.Timer);
        Fade(ChargingPanel, showPanels && mode == Mode.Charging);
        Fade(PinPanel,      showPanels && mode == Mode.PinTarget);
        Fade(MascotPanel,   _settingsService.MascotEnabled && mode != Mode.Notify);
        UpdateBars();
    }

    public void SetClockFormat(bool use24h)
    {
        _settingsService.SetClockFormat(use24h);
        _item12.Checked = !use24h;
        _item24.Checked = use24h;
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        bool use24h = _settingsService.Use24HourClock;
        if (_rot == 0)
        {
            TimeRun.Text = now.ToString(use24h ? "HH:mm" : "h:mm tt");
            DateRun.Text = "   " + now.ToString("ddd, MMM d");
        }
        else
        {
            TimeRun.Text = now.ToString(use24h ? "HH\nmm" : "h\nmm\ntt");
            DateRun.Text = "\n" + now.ToString("ddd\nMMM\nd");
        }

        BigTime.Text = now.ToString(use24h ? "HH:mm" : "h:mm");
        BigAmPm.Text = use24h ? "" : now.ToString(" tt");
        BigDate.Text = now.ToString("dddd, MMMM d");
        UpdateClockBattery();

        _clock.Stop();
        _clock.Interval = TimeSpan.FromMilliseconds(60_000 - (now.Second * 1000 + now.Millisecond) + 50);
        _clock.Start();
    }

        private void DismissNotification()
    {
        _revert.Stop();
        var (targetModeName, targetView) = IslandLogic.DetermineNotificationDismissTarget(
            _hover,
            _preNotifyMode.ToString(),
            _preNotifyView,
            _playing,
            _timerService.IsActive);

        _view = targetView;
        ApplyView();

        var targetMode = Enum.TryParse<Mode>(targetModeName, out var m) ? m : Rest();
        Go(targetMode);
    }

    private Mode Rest() => _timerService.IsActive ? Mode.Timer : _playing ? Mode.Compact : Mode.Idle;

    private void SyncRest()
    {
        if (_mode is Mode.Idle or Mode.Compact or Mode.Timer)
        {
            _view = IslandLogic.GetInitialExpandedView(_playing);
            ApplyView();
            Go(Rest());
        }
        UpdateBars();
    }

    private void UpdateBars()
    {
        bool run = (!_settingsService.MascotEnabled || _mascotExpanded) && _playing && (_mode is Mode.Compact or Mode.Timer or Mode.Media) && SystemParameters.ClientAreaAnimation;
        _barsOn = run;

        ScaleTransform[] activeBars = _mode switch
        {
            Mode.Compact => new[] { B1, B2, B3, B4 },
            Mode.Timer   => new[] { TB1, TB2, TB3, TB4 },
            Mode.Media   => new[] { EB1, EB2, EB3, EB4 },
            _            => Array.Empty<ScaleTransform>()
        };

        var allBars = new[] { B1, B2, B3, B4, TB1, TB2, TB3, TB4, EB1, EB2, EB3, EB4 };
        double[] ms = { 420, 560, 480, 640 };
        double[] top = { 1.0, 0.85, 1.0, 0.9 };

        foreach (var barTransform in allBars)
        {
            int activeIdx = run ? Array.IndexOf(activeBars, barTransform) : -1;
            if (activeIdx >= 0)
            {
                var bar = new DoubleAnimation(0.2, top[activeIdx], TimeSpan.FromMilliseconds(ms[activeIdx]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase()
                };
                Timeline.SetDesiredFrameRate(bar, 30);
                barTransform.BeginAnimation(ScaleTransform.ScaleYProperty, bar);
            }
            else
            {
                barTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                barTransform.ScaleY = 0.4;
            }
        }
    }

    // Panel swap as a gsap timeline: outgoing fades fast, incoming fades + scales in with a small overshoot.
    private void Fade(UIElement panel, bool show)
    {
        panel.IsHitTestVisible = show;
        var scale = panel == MascotPanel ? MascotFadeScale : (ScaleTransform)panel.RenderTransform;
        double o0 = panel.Opacity, s0 = scale.ScaleX;

        var tl = new GsapTimeline();
        if (show)
        {
            tl.To(0.3, Ease.PowerOut(2), p => panel.Opacity = o0 + (1 - o0) * p, 0.1)
              .To(0.55, Ease.BackOut(1.4), p => scale.ScaleX = scale.ScaleY = s0 + (1 - s0) * p, 0.1);
        }
        else
        {
            tl.To(0.12, Ease.PowerOut(2), p => panel.Opacity = o0 * (1 - p), 0)
              .To(0.18, Ease.PowerOut(2), p => scale.ScaleX = scale.ScaleY = s0 + (0.9 - s0) * p, 0);
        }
        Gsap.Play(panel, "fade", tl);
    }

    // Slide the page content that was just made visible in, keyed to scroll direction; buttons pop in staggered.
    private void RevealView(int dir, double delay)
    {
        double dy = (dir == 0 ? 1 : dir) * 12;
        switch ((IslandLogic.ExpandedView)_view)
        {
            case IslandLogic.ExpandedView.Clock:
                Fx.Reveal(ClockView, dy, delay);
                break;
            case IslandLogic.ExpandedView.Music:
                Fx.Reveal(MediaMusicView, dy, delay);
                var ctl = new System.Collections.Generic.List<FrameworkElement>();
                if (ShuffleBtn != null && ShuffleBtn.Visibility == Visibility.Visible) ctl.Add(ShuffleBtn);
                if (PrevBtn != null && PrevBtn.Visibility == Visibility.Visible) ctl.Add(PrevBtn);
                if (PlayPauseBtn != null && PlayPauseBtn.Visibility == Visibility.Visible) ctl.Add(PlayPauseBtn);
                if (NextBtn != null && NextBtn.Visibility == Visibility.Visible) ctl.Add(NextBtn);
                if (RepeatBtn != null && RepeatBtn.Visibility == Visibility.Visible) ctl.Add(RepeatBtn);
                Fx.PopStagger(ctl, delay + 0.06);
                break;
            case IslandLogic.ExpandedView.Timer:
                Fx.Reveal(TimerView, dy, delay);
                break;
            case IslandLogic.ExpandedView.Clipboard:
                Fx.Reveal(ClipboardView, dy, delay);
                break;
        }
    }

    private void SetAnchor()
    {
        foreach (var p in new FrameworkElement[] { IdlePanel, CompactPanel, MediaPanel, VolumePanel, NotifyPanel, TimerPanel, ChargingPanel, PinPanel })
        {
            p.VerticalAlignment = _bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            p.Margin = _bottom ? new Thickness(0, 0, 0, TopGap) : new Thickness(0, TopGap, 0, 0);
        }
    }

    private void ApplyView()
    {
        ClockView.Visibility = _view == (int)IslandLogic.ExpandedView.Clock ? Visibility.Visible : Visibility.Hidden;
        MediaMusicView.Visibility = _view == (int)IslandLogic.ExpandedView.Music ? Visibility.Visible : Visibility.Hidden;
        TimerView.Visibility = _view == (int)IslandLogic.ExpandedView.Timer ? Visibility.Visible : Visibility.Hidden;
        ClipboardView.Visibility = _view == (int)IslandLogic.ExpandedView.Clipboard ? Visibility.Visible : Visibility.Hidden;
        if (MediaMusicView != null)
        {
            MediaMusicView.Margin = _settingsService.MascotEnabled ? new Thickness(0, 48, 0, 0) : new Thickness(0, 20, 0, 0);
        }
        // mascot sits at the top of the pill: push clock/timer/clipboard below it (shrunk to fit)
        bool m = _settingsService.MascotEnabled;
        foreach (var v in new FrameworkElement[] { ClockView, TimerView })
        {
            v.VerticalAlignment = m ? VerticalAlignment.Top : VerticalAlignment.Center;
            v.Margin = m ? new Thickness(0, 52, 0, 0) : new Thickness(0);
            v.LayoutTransform = m ? new ScaleTransform(0.78, 0.78) : Transform.Identity;
        }
        ClipboardView.Margin = m ? new Thickness(18, 50, 18, 8) : new Thickness(18, 10, 18, 10);
        if (_view == (int)IslandLogic.ExpandedView.Clock) UpdateClockBattery();
        if (_view == (int)IslandLogic.ExpandedView.Timer) RefreshTimerView();
        if (_view == (int)IslandLogic.ExpandedView.Clipboard) RefreshClipboardView();
    }

    private void RefreshClipboardView()
    {
        string latest = _clipboardService.LatestText;
        int count = _clipboardService.Count;
        ClipCountBadge.Text = count == 1 ? "1 item" : $"{count} items";

        if (string.IsNullOrEmpty(latest))
        {
            ClipPreviewText.Text = "No copied text yet";
            ClipPreviewText.Opacity = 0.5;
            ClipMetaText.Text = "Copy any text in Windows to store here";
            ClipQuickCopyBtn.IsEnabled = false;
            ClipQuickCopyBtn.Opacity = 0.4;
        }
        else
        {
            ClipPreviewText.Text = IslandLogic.FormatClipboardPreview(latest, 80);
            ClipPreviewText.Opacity = 1.0;
            ClipMetaText.Text = $"{latest.Length:N0} characters" + (latest.Contains('\n') ? " • multi-line" : " • single-line");
            ClipQuickCopyBtn.IsEnabled = true;
            ClipQuickCopyBtn.Opacity = 1.0;
        }
    }

    private void OnOpenClipboardHistory(object sender, RoutedEventArgs e)
    {
        ClipboardDialog.ShowHistory(_clipboardService, this);
    }

    private async void OnQuickCopyLatest(object sender, RoutedEventArgs e)
    {
        string latest = _clipboardService.LatestText;
        if (string.IsNullOrEmpty(latest)) return;

        if (_clipboardService.CopyToClipboard(latest))
        {
            ClipQuickCopyGlyph.Text = "\uE73E"; // Checkmark
            ClipQuickCopyGlyph.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58)); // Apple Green
            await Task.Delay(1000);
            ClipQuickCopyGlyph.Text = "\uE8C8";
            ClipQuickCopyGlyph.Foreground = Brushes.White;
        }
    }

    private void RefreshTimerView()
    {
        bool run = _timerService.IsActive;
        TimerBig.Text = run ? TimerRun.Text : _setMin.ToString();
        TimerUnit.Text = run ? "" : " min";
        TMinus.Visibility = TPlus.Visibility = run ? Visibility.Hidden : Visibility.Visible;
        TimerGo.Content = run ? "Cancel" : "Start focus";
    }

    private void OnTimerMinus(object sender, RoutedEventArgs e) { _setMin = Math.Max(5, _setMin - 5); RefreshTimerView(); }
    private void OnTimerPlus(object sender, RoutedEventArgs e)  { _setMin = Math.Min(180, _setMin + 5); RefreshTimerView(); }
    private void OnTimerGo(object sender, RoutedEventArgs e)
    {
        if (_timerService.IsActive) CancelTimer(); else StartTimer(_setMin);
        RefreshTimerView();
    }

    private void ResetPosition()
    {
        _userMoved = false;
        _rot = 0; _bottom = false; SetAnchor();
        Root.LayoutTransform = Transform.Identity;
        IdleText.LayoutTransform = Transform.Identity;
        Width = 440; Height = 160;
        Position();
        UpdateClock();
        Animate(_mode);
    }

    private const double SNAP_THRESHOLD = 100; // px from edge to trigger snap

    private void SnapToEdge()
    {
        double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight;
        double d = ShapeGeo.Rect.Height / 2;
        double cx = _rot == 0 ? Left + 220 : Left + (_rot < 0 ? 6 + d : 154 - d);
        double cy = _rot == 0 ? (_bottom ? Top + 154 - d : Top + 6 + d) : Top + 220;
        double min = Math.Min(Math.Min(cy, H - cy), Math.Min(cx, W - cx));

        // Not close enough to any edge: un-rotate and keep the position the user dragged to.
        if (min >= SNAP_THRESHOLD)
        {
            if (_rot != 0 || _bottom)
            {
                _rot = 0;
                _bottom = false;
                SetAnchor();
                Root.LayoutTransform = Transform.Identity;
                IdleText.LayoutTransform = Transform.Identity;
                Width = 440;
                Height = 160;
                UpdateClock();
                Animate(_mode);
            }
            return;
        }

        _rot = min == cx ? -90 : min == W - cx ? 90 : 0;
        _bottom = _rot == 0 && min == H - cy;
        SetAnchor();
        Root.LayoutTransform = new RotateTransform(_rot);
        IdleText.LayoutTransform = new RotateTransform(-_rot);
        Width = _rot == 0 ? 440 : 160;
        Height = _rot == 0 ? 160 : 440;
        if (_rot == 0) { Top = _bottom ? H - 160 : 0; Left = Math.Clamp(cx - 220, -125, W - 315); }
        else { Left = _rot < 0 ? 0 : W - 160; Top = Math.Clamp(cy - 220, -125, H - 315); }
        UpdateClock();
        Animate(_mode);
    }

    private void OnIslandEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hover = true;
        if (_mode == Mode.Notify) return;
        if (_settingsService.MascotEnabled) return;
        _view = IslandLogic.GetInitialExpandedView(_playing);
        ApplyView();
        EnforceTopmost();
        Go(Mode.Media);
    }

    private void OnIslandLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hover = false;
        _mascotExpanded = false;
        if (_mode == Mode.Notify) return;
        if (_settingsService.MascotEnabled)
        {
            if (_mode == Mode.Media) Go(Rest(), 0.8);
            return;
        }
        Go(_mode, 0.8);
    }
}











