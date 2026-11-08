// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Encapsulates application configuration, clock preferences, and Windows autostart registration.
/// </summary>
public sealed class SettingsService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string RunValueName = "DynamicIsland";

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "clock.txt");

    private static readonly string BatterySettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "charging_always_on.txt");

    private static readonly string AlwaysOnTopSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "always_on_top.txt");

    private static readonly string PushToTalkSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "push_to_talk_key.txt");

    private static readonly string WakeWordSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "wake_word_enabled.txt");

    private static readonly string FocusTimerSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "focus_timer_minutes.txt");

    private static readonly string WakePhraseSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "wake_phrase.txt");

    private static readonly string PushToTalkEnabledSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "push_to_talk_enabled.txt");

    private static readonly string PushToTalkNameSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "push_to_talk_name.txt");

    private static readonly string PushToTalkModSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "push_to_talk_mod.txt");

    private static readonly string MicrophoneDeviceSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "microphone_device.txt");

    private static readonly string MicrophoneSensitivitySettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "microphone_sensitivity.txt");

    private static readonly string IslandScaleSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "island_scale.txt");

    private static readonly string IslandPositionSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "island_position.txt");

    private static readonly string TelemetrySettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "telemetry_enabled.txt");

    public bool Use24HourClock { get; private set; }
    public bool AlwaysShowChargingIndicator { get; private set; }
    public bool UseBackdropBlur { get; private set; } = true;
    public bool AlwaysOnTopFullscreen { get; private set; } = true;
    public int PushToTalkVirtualKey { get; private set; } = 0x14; // Default Caps Lock
    public int PushToTalkModifierKey { get; private set; } = 0;
    public string PushToTalkShortcutName { get; private set; } = "[Caps Lock]";
    public bool PushToTalkEnabled { get; private set; } = true;
    public bool WakeWordEnabled { get; private set; } = true;
    public string WakePhrase { get; private set; } = "Hey Nixi";
    public int FocusTimerDefaultMinutes { get; private set; } = 25;
    public string MicrophoneDeviceId { get; private set; } = "";
    public int MicrophoneSensitivity { get; private set; } = 72;
    public double IslandScale { get; private set; } = 1.0;
    public string IslandPosition { get; private set; } = "center";
    public bool AnonymousTelemetryEnabled { get; private set; } = false;
    public bool MascotEnabled { get; private set; }
    private static readonly string MascotSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_enabled.txt");
    private static readonly string MascotAccessorySettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_accessory.txt");
    public string MascotAccessory { get; private set; } = "none";
    private static readonly string MascotDeepSleepFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_deepsleep.txt");
    /// <summary>When true, a sleeping mascot ignores mouse movement and only wakes when tapped.</summary>
    public bool MascotDeepSleep { get; private set; } = true;
    private static readonly string MascotNeverSleepFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_never_sleep.txt");
    /// <summary>When true, the mascot stays awake and never falls asleep.</summary>
    public bool MascotNeverSleep { get; private set; } = false;
    private static readonly string MascotPrivacyIndicatorsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_privacy_indicators.txt");
    /// <summary>When false, microphone and camera privacy dots are hidden while the mascot is enabled.</summary>
    public bool MascotPrivacyIndicatorsEnabled { get; private set; } = true;
    private static readonly string MascotListenStyleFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_listen.txt");
    /// <summary>Listening animation: "glow" (comet ring), "ripple", "rainbow" or "waves" (arcs beside the pill).</summary>
    public string MascotListenStyle { get; private set; } = "glow";
    private static readonly string MascotEyeColorSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "mascot_eye_color.txt");
    public string MascotEyeColor { get; private set; } = "#829BFF";
    public string ModelProvider { get; private set; } = "cloud";   // "cloud" (Gemini) | "local" (Ollama on this PC)
    public string LocalModelName { get; private set; } = "qwen2.5:7b";
    public string LocalModelEndpoint { get; private set; } = "http://localhost:11434/v1";

    public event Action<bool>? ClockFormatChanged;
    public event Action<int>? FocusTimerDurationChanged;
    public event Action<double>? IslandScaleChanged;
    public event Action<string>? IslandPositionChanged;

    public SettingsService()
    {
        LoadClockFormat();
        LoadChargingIndicator();
        LoadAlwaysOnTop();
        LoadPushToTalkKey();
        LoadWakeWordEnabled();
        LoadFocusTimerMinutes();
        LoadWakePhrase();
        LoadPushToTalkEnabled();
        LoadPushToTalkShortcut();
        LoadMicrophoneSettings();
        LoadIslandDisplaySettings();
        LoadTelemetryEnabled();
        try { if (File.Exists(MascotListenStyleFile)) SetMascotListenStyle(File.ReadAllText(MascotListenStyleFile).Trim(), save: false); } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(MascotDeepSleepFile)) MascotDeepSleep = File.ReadAllText(MascotDeepSleepFile).Trim() != "0"; } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(MascotNeverSleepFile)) MascotNeverSleep = File.ReadAllText(MascotNeverSleepFile).Trim() == "1"; } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(MascotPrivacyIndicatorsFile)) MascotPrivacyIndicatorsEnabled = File.ReadAllText(MascotPrivacyIndicatorsFile).Trim() != "0"; } catch (Exception ex) { Program.Log(ex); }
        try { MascotEnabled = File.Exists(MascotSettingsFile) && File.ReadAllText(MascotSettingsFile).Trim() == "1"; } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(MascotAccessorySettingsFile)) SetMascotAccessory(File.ReadAllText(MascotAccessorySettingsFile).Trim(), save: false); } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(MascotEyeColorSettingsFile)) SetMascotEyeColor(File.ReadAllText(MascotEyeColorSettingsFile).Trim(), save: false); } catch (Exception ex) { Program.Log(ex); }
        try { if (File.Exists(BlurSettingsFile)) UseBackdropBlur = File.ReadAllText(BlurSettingsFile).Trim() != "0"; }
        catch { UseBackdropBlur = true; }
        LoadModelSettings();
    }

    private void LoadWakeWordEnabled()
    {
        try
        {
            if (File.Exists(WakeWordSettingsFile))
            {
                WakeWordEnabled = File.ReadAllText(WakeWordSettingsFile).Trim() != "0";
            }
            else
            {
                WakeWordEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            WakeWordEnabled = true;
        }
    }

    public void SetWakeWordEnabled(bool enable)
    {
        WakeWordEnabled = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WakeWordSettingsFile)!);
            File.WriteAllText(WakeWordSettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadPushToTalkKey()
    {
        try
        {
            if (File.Exists(PushToTalkSettingsFile) &&
                int.TryParse(File.ReadAllText(PushToTalkSettingsFile).Trim(), out int key) &&
                key is >= 0 and < 256)
            {
                PushToTalkVirtualKey = key;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetPushToTalkVirtualKey(int virtualKey)
    {
        if (virtualKey is < 0 or >= 256) return;
        PushToTalkVirtualKey = virtualKey;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PushToTalkSettingsFile)!);
            File.WriteAllText(PushToTalkSettingsFile, virtualKey.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadPushToTalkShortcut()
    {
        try
        {
            if (File.Exists(PushToTalkNameSettingsFile))
            {
                string name = File.ReadAllText(PushToTalkNameSettingsFile).Trim();
                if (!string.IsNullOrEmpty(name)) PushToTalkShortcutName = name;
            }
            if (File.Exists(PushToTalkModSettingsFile) &&
                int.TryParse(File.ReadAllText(PushToTalkModSettingsFile).Trim(), out int mod))
            {
                PushToTalkModifierKey = mod;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetPushToTalkShortcut(string name, int vk, int mod)
    {
        PushToTalkShortcutName = name;
        PushToTalkVirtualKey = vk;
        PushToTalkModifierKey = mod;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PushToTalkNameSettingsFile)!);
            File.WriteAllText(PushToTalkNameSettingsFile, name);
            File.WriteAllText(PushToTalkSettingsFile, vk.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(PushToTalkModSettingsFile, mod.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadPushToTalkEnabled()
    {
        try
        {
            if (File.Exists(PushToTalkEnabledSettingsFile))
                PushToTalkEnabled = File.ReadAllText(PushToTalkEnabledSettingsFile).Trim() != "0";
            else
                PushToTalkEnabled = true;
        }
        catch (Exception ex) { Program.Log(ex); PushToTalkEnabled = true; }
    }

    public void SetPushToTalkEnabled(bool enable)
    {
        PushToTalkEnabled = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PushToTalkEnabledSettingsFile)!);
            File.WriteAllText(PushToTalkEnabledSettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadWakePhrase()
    {
        try
        {
            if (File.Exists(WakePhraseSettingsFile))
            {
                string phrase = File.ReadAllText(WakePhraseSettingsFile).Trim();
                if (!string.IsNullOrEmpty(phrase)) WakePhrase = phrase;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetWakePhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;
        WakePhrase = phrase.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WakePhraseSettingsFile)!);
            File.WriteAllText(WakePhraseSettingsFile, WakePhrase);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadFocusTimerMinutes()
    {
        try
        {
            if (File.Exists(FocusTimerSettingsFile) &&
                int.TryParse(File.ReadAllText(FocusTimerSettingsFile).Trim(), out int min) &&
                min is > 0 and <= 360)
            {
                FocusTimerDefaultMinutes = min;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetFocusTimerDefaultMinutes(int minutes)
    {
        if (minutes is <= 0 or > 360) return;
        FocusTimerDefaultMinutes = minutes;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FocusTimerSettingsFile)!);
            File.WriteAllText(FocusTimerSettingsFile, minutes.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Program.Log(ex); }
        FocusTimerDurationChanged?.Invoke(minutes);
    }

    private void LoadMicrophoneSettings()
    {
        try
        {
            if (File.Exists(MicrophoneDeviceSettingsFile))
            {
                MicrophoneDeviceId = File.ReadAllText(MicrophoneDeviceSettingsFile).Trim();
            }
            if (File.Exists(MicrophoneSensitivitySettingsFile) &&
                int.TryParse(File.ReadAllText(MicrophoneSensitivitySettingsFile).Trim(), out int sens) &&
                sens is >= 0 and <= 100)
            {
                MicrophoneSensitivity = sens;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMicrophoneDevice(string deviceId)
    {
        MicrophoneDeviceId = deviceId ?? "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MicrophoneDeviceSettingsFile)!);
            File.WriteAllText(MicrophoneDeviceSettingsFile, MicrophoneDeviceId);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMicrophoneSensitivity(int sensitivity)
    {
        MicrophoneSensitivity = Math.Clamp(sensitivity, 0, 100);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MicrophoneSensitivitySettingsFile)!);
            File.WriteAllText(MicrophoneSensitivitySettingsFile, MicrophoneSensitivity.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadIslandDisplaySettings()
    {
        try
        {
            if (File.Exists(IslandScaleSettingsFile) &&
                double.TryParse(File.ReadAllText(IslandScaleSettingsFile).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double sc) &&
                sc is >= 0.5 and <= 2.0)
            {
                IslandScale = sc;
            }
            if (File.Exists(IslandPositionSettingsFile))
            {
                string pos = File.ReadAllText(IslandPositionSettingsFile).Trim().ToLowerInvariant();
                if (pos is "left" or "center" or "right") IslandPosition = pos;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetIslandScale(double scale)
    {
        if (scale is < 0.5 or > 2.0) return;
        IslandScale = scale;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IslandScaleSettingsFile)!);
            File.WriteAllText(IslandScaleSettingsFile, scale.ToString("0.00", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Program.Log(ex); }
        IslandScaleChanged?.Invoke(scale);
    }

    public void SetIslandPosition(string position)
    {
        if (position is not ("left" or "center" or "right")) return;
        IslandPosition = position;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IslandPositionSettingsFile)!);
            File.WriteAllText(IslandPositionSettingsFile, position);
        }
        catch (Exception ex) { Program.Log(ex); }
        IslandPositionChanged?.Invoke(position);
    }

    public void SetMascotAccessory(string accessory, bool save = true)
    {
        if (accessory is not ("none" or "shades" or "bow")) return;
        MascotAccessory = accessory;
        if (!save) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotAccessorySettingsFile)!); File.WriteAllText(MascotAccessorySettingsFile, accessory); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotListenStyle(string style, bool save = true)
    {
        if (style is not ("glow" or "ripple" or "rainbow" or "waves")) return;
        MascotListenStyle = style;
        if (!save) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotListenStyleFile)!); File.WriteAllText(MascotListenStyleFile, style); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotDeepSleep(bool on)
    {
        MascotDeepSleep = on;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotDeepSleepFile)!); File.WriteAllText(MascotDeepSleepFile, on ? "1" : "0"); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotNeverSleep(bool on)
    {
        MascotNeverSleep = on;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotNeverSleepFile)!); File.WriteAllText(MascotNeverSleepFile, on ? "1" : "0"); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotPrivacyIndicatorsEnabled(bool on)
    {
        MascotPrivacyIndicatorsEnabled = on;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotPrivacyIndicatorsFile)!); File.WriteAllText(MascotPrivacyIndicatorsFile, on ? "1" : "0"); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotEnabled(bool enabled)
    {
        MascotEnabled = enabled;
        try { Directory.CreateDirectory(Path.GetDirectoryName(MascotSettingsFile)!); File.WriteAllText(MascotSettingsFile, enabled ? "1" : "0"); }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetMascotEyeColor(string hexColor, bool save = true)
    {
        if (string.IsNullOrWhiteSpace(hexColor)) return;
        hexColor = hexColor.Trim();
        if (!hexColor.StartsWith('#')) hexColor = "#" + hexColor;
        MascotEyeColor = hexColor;
        if (!save) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MascotEyeColorSettingsFile)!);
            File.WriteAllText(MascotEyeColorSettingsFile, MascotEyeColor);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadTelemetryEnabled()
    {
        try
        {
            if (File.Exists(TelemetrySettingsFile))
                AnonymousTelemetryEnabled = File.ReadAllText(TelemetrySettingsFile).Trim() == "1";
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetAnonymousTelemetryEnabled(bool enable)
    {
        AnonymousTelemetryEnabled = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TelemetrySettingsFile)!);
            File.WriteAllText(TelemetrySettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadAlwaysOnTop()
    {
        try
        {
            if (File.Exists(AlwaysOnTopSettingsFile))
            {
                AlwaysOnTopFullscreen = File.ReadAllText(AlwaysOnTopSettingsFile).Trim() != "0";
            }
            else
            {
                AlwaysOnTopFullscreen = true;
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            AlwaysOnTopFullscreen = true;
        }
    }

    public void SetAlwaysOnTopFullscreen(bool enable)
    {
        AlwaysOnTopFullscreen = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AlwaysOnTopSettingsFile)!);
            File.WriteAllText(AlwaysOnTopSettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private static readonly string BlurSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "backdrop_blur.txt");

    public void SetBackdropBlur(bool enable)
    {
        UseBackdropBlur = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BlurSettingsFile)!);
            File.WriteAllText(BlurSettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private static readonly string ModelProviderSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "model_provider.txt");
    private static readonly string LocalModelNameSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "local_model.txt");
    private static readonly string LocalModelEndpointSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "local_endpoint.txt");

    private void LoadModelSettings()
    {
        try
        {
            if (File.Exists(ModelProviderSettingsFile))
            {
                var v = File.ReadAllText(ModelProviderSettingsFile).Trim();
                if (v is "local" or "cloud") ModelProvider = v;
            }
            if (File.Exists(LocalModelNameSettingsFile))
            {
                var v = File.ReadAllText(LocalModelNameSettingsFile).Trim();
                if (!string.IsNullOrWhiteSpace(v)) LocalModelName = v;
            }
            if (File.Exists(LocalModelEndpointSettingsFile))
            {
                var v = File.ReadAllText(LocalModelEndpointSettingsFile).Trim();
                if (!string.IsNullOrWhiteSpace(v)) LocalModelEndpoint = v;
            }
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetModelProvider(string provider)
    {
        ModelProvider = provider == "local" ? "local" : "cloud";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelProviderSettingsFile)!);
            File.WriteAllText(ModelProviderSettingsFile, ModelProvider);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetLocalModelName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        LocalModelName = name.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalModelNameSettingsFile)!);
            File.WriteAllText(LocalModelNameSettingsFile, LocalModelName);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public void SetLocalModelEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        LocalModelEndpoint = endpoint.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocalModelEndpointSettingsFile)!);
            File.WriteAllText(LocalModelEndpointSettingsFile, LocalModelEndpoint);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    private void LoadClockFormat()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                Use24HourClock = File.ReadAllText(SettingsFile).Trim() == "24";
            }
            else
            {
                Use24HourClock = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            Use24HourClock = false;
        }
    }

    public void SetClockFormat(bool use24h)
    {
        Use24HourClock = use24h;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, use24h ? "24" : "12");
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        ClockFormatChanged?.Invoke(use24h);
    }

    private void LoadChargingIndicator()
    {
        try
        {
            if (File.Exists(BatterySettingsFile))
            {
                AlwaysShowChargingIndicator = File.ReadAllText(BatterySettingsFile).Trim() == "1";
            }
            else
            {
                AlwaysShowChargingIndicator = false;
            }
        }
        catch
        {
            AlwaysShowChargingIndicator = false;
        }
    }

    public void SetAlwaysShowChargingIndicator(bool enable)
    {
        AlwaysShowChargingIndicator = enable;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BatterySettingsFile)!);
            File.WriteAllText(BatterySettingsFile, enable ? "1" : "0");
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    public string? GetApplicationExecutablePath()
    {
        try
        {
            // 1. Current process executable if it is DynamicIsland.exe
            string? proc = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(proc) &&
                File.Exists(proc) &&
                Path.GetFileName(proc).Equals("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(proc);
            }

            // 2. BaseDirectory (where app domain is running)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string directExe = Path.Combine(baseDir, "DynamicIsland.exe");
            if (File.Exists(directExe))
            {
                return Path.GetFullPath(directExe);
            }

            // 3. Check published binary in project workspace if running from build folder
            string publishExe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "publish", "DynamicIsland.exe"));
            if (File.Exists(publishExe))
            {
                return publishExe;
            }

            // 4. Fallback to process main module
            string? mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainModule) &&
                File.Exists(mainModule) &&
                Path.GetFileName(mainModule).Equals("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(mainModule);
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }

        return Environment.ProcessPath;
    }

    public static string GetStartupShortcutPath()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, "DynamicIsland.lnk");
    }

    public static string GetStartupCmdPath()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, "DynamicIsland.cmd");
    }

    private static bool CreateStartupShortcut(string targetPath)
    {
        try
        {
            string shortcutPath = GetStartupShortcutPath();
            string? dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
                    shortcut.Description = "Dynamic Island for Windows";
                    shortcut.Save();
                    return File.Exists(shortcutPath);
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log($"CreateStartupShortcut exception: {ex.Message}");
        }

        return false;
    }

    private static bool CreateStartupCmd(string targetPath)
    {
        try
        {
            string cmdPath = GetStartupCmdPath();
            string workDir = Path.GetDirectoryName(targetPath) ?? "";
            string content = $"@start \"\" /d \"{workDir}\" \"{targetPath}\"\r\n";
            File.WriteAllText(cmdPath, content);
            return File.Exists(cmdPath);
        }
        catch (Exception ex)
        {
            Program.Log($"CreateStartupCmd exception: {ex.Message}");
            return false;
        }
    }

    private static void UnblockStartupApproved()
    {
        // 02 00 00 00 00 00 00 00 00 00 00 00 is Windows standard 12-byte enabled flag for StartupApproved
        byte[] enabledBytes = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        try
        {
            using var approvedRun = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKey);
            approvedRun?.SetValue(RunValueName, enabledBytes, RegistryValueKind.Binary);
        }
        catch { /* best effort */ }

        try
        {
            using var approvedFolder = Registry.CurrentUser.CreateSubKey(StartupApprovedFolderKey);
            approvedFolder?.SetValue("DynamicIsland.lnk", enabledBytes, RegistryValueKind.Binary);
            approvedFolder?.SetValue("DynamicIsland.cmd", enabledBytes, RegistryValueKind.Binary);
        }
        catch { /* best effort */ }
    }

    private static void ClearStartupApproved()
    {
        try
        {
            using var approvedRun = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, true);
            approvedRun?.DeleteValue(RunValueName, false);
        }
        catch { /* best effort */ }

        try
        {
            using var approvedFolder = Registry.CurrentUser.OpenSubKey(StartupApprovedFolderKey, true);
            approvedFolder?.DeleteValue("DynamicIsland.lnk", false);
            approvedFolder?.DeleteValue("DynamicIsland.cmd", false);
        }
        catch { /* best effort */ }
    }

    public bool IsStartWithWindowsEnabled()
    {
        try
        {
            // 1. Check Startup folder shortcut or cmd
            string shortcutPath = GetStartupShortcutPath();
            if (File.Exists(shortcutPath))
            {
                return true;
            }

            string cmdPath = GetStartupCmdPath();
            if (File.Exists(cmdPath))
            {
                return true;
            }

            // 2. Check Run registry key
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey);
            object? val = key?.GetValue(RunValueName);
            if (val is string str && !string.IsNullOrWhiteSpace(str))
            {
                string clean = str.Trim().Trim('"');
                if (File.Exists(clean))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }
    }

    public bool SetStartWithWindows(bool enable)
    {
        bool anySuccess = false;
        try
        {
            string shortcutPath = GetStartupShortcutPath();
            string cmdPath = GetStartupCmdPath();

            if (enable)
            {
                string? exePath = GetApplicationExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Program.Log($"SetStartWithWindows: executable path not found: {exePath}");
                    return false;
                }

                // 1. Write Registry Run key
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunRegistryKey);
                    if (key != null)
                    {
                        key.SetValue(RunValueName, $"\"{exePath}\"");
                        anySuccess = true;
                    }
                }
                catch (Exception ex)
                {
                    Program.Log($"SetStartWithWindows Registry write error: {ex.Message}");
                }

                // 2. Unblock in StartupApproved
                UnblockStartupApproved();

                // 3. Create Startup folder shortcut (or .cmd fallback)
                bool shortcutCreated = CreateStartupShortcut(exePath);
                if (!shortcutCreated)
                {
                    shortcutCreated = CreateStartupCmd(exePath);
                }
                if (shortcutCreated)
                {
                    anySuccess = true;
                }
            }
            else
            {
                // 1. Delete from Registry Run key
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunRegistryKey);
                    key?.DeleteValue(RunValueName, false);
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    Program.Log($"SetStartWithWindows Registry delete error: {ex.Message}");
                }

                // 2. Clear StartupApproved entries
                ClearStartupApproved();

                // 3. Delete Startup folder files
                try
                {
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    if (File.Exists(cmdPath)) File.Delete(cmdPath);
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    Program.Log($"SetStartWithWindows Startup folder delete error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }

        return anySuccess;
    }

    public void EnsureStartupPathUpToDate()
    {
        try
        {
            if (IsStartWithWindowsEnabled())
            {
                // Re-register with current active executable path to heal stale paths or registry keys
                SetStartWithWindows(true);
            }
        }
        catch (Exception ex)
        {
            Program.Log($"EnsureStartupPathUpToDate error: {ex.Message}");
        }
    }
}
