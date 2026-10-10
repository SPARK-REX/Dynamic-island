// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using DynamicIsland;
using DynamicIsland.Services;

namespace DynamicIsland.Tests;

internal static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    private static void AssertTest(string name, bool condition)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [PASS] ");
            Console.ResetColor();
            Console.WriteLine(name);
            _passed++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [FAIL] ");
            Console.ResetColor();
            Console.WriteLine(name);
            _failed++;
        }
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("\n=== Dynamic Island Unit Test Suite (DynamicIsland.Tests) ===");

        // 1. Volume & Audio Math Tests
        AssertTest("Volume percent normal (0.50 -> 50)", IslandLogic.Percent(0.50f, false) == 50);
        AssertTest("Volume percent ceiling (1.00 -> 100)", IslandLogic.Percent(1.00f, false) == 100);
        AssertTest("Volume percent clamp upper (1.50 -> 100)", IslandLogic.Percent(1.50f, false) == 100);
        AssertTest("Volume percent clamp lower (-0.50 -> 0)", IslandLogic.Percent(-0.50f, false) == 0);
        AssertTest("Volume muted reads as 0 (0.80, muted -> 0)", IslandLogic.Percent(0.80f, true) == 0);
        AssertTest("Volume float.NaN reads as 0", IslandLogic.Percent(float.NaN, false) == 0);
        AssertTest("Volume float.PositiveInfinity reads as 0", IslandLogic.Percent(float.PositiveInfinity, false) == 0);
        AssertTest("Volume glyph speaker when active", IslandLogic.VolumeGlyph(0.6f, false) == "\uE767");
        AssertTest("Volume glyph mute when muted", IslandLogic.VolumeGlyph(0.6f, true) == "\uE74F");
        AssertTest("Volume glyph mute when 0 volume", IslandLogic.VolumeGlyph(0f, false) == "\uE74F");
        AssertTest("Volume glyph mute when NaN volume", IslandLogic.VolumeGlyph(float.NaN, false) == "\uE74F");
        AssertTest("PlayPause glyph shows Pause icon (E769) when playing", IslandLogic.PlayPauseGlyph(true) == IslandLogic.GlyphPause);
        AssertTest("PlayPause glyph shows Play icon (E768) when paused", IslandLogic.PlayPauseGlyph(false) == IslandLogic.GlyphPlay);

        // 2. Timer Formatting Tests
        AssertTest("Timer format 25 minutes (25:00)", IslandLogic.FormatTimer(TimeSpan.FromMinutes(25)) == "25:00");
        AssertTest("Timer format zero time (00:00)", IslandLogic.FormatTimer(TimeSpan.Zero) == "00:00");
        AssertTest("Timer format negative time (00:00)", IslandLogic.FormatTimer(TimeSpan.FromSeconds(-5)) == "00:00");
        AssertTest("Timer format hours (1:05:30)", IslandLogic.FormatTimer(new TimeSpan(1, 5, 30)) == "1:05:30");
        AssertTest("Timer format multi-hour (2:00:00)", IslandLogic.FormatTimer(TimeSpan.FromHours(2)) == "2:00:00");
        AssertTest("Timer format 24+ hours (25:00:00)", IslandLogic.FormatTimer(TimeSpan.FromHours(25)) == "25:00:00");

        // 3. Pill Dimensions & Geometry
        var (idleW, idleH, idleR) = IslandLogic.GetPillDimensions("Idle", false, false);
        AssertTest("Pill idle dimensions (190x34, r=17)", idleW == 190.0 && idleH == 34.0 && idleR == 17.0);

        var (medW, medH, medR) = IslandLogic.GetPillDimensions("Media", false, false);
        AssertTest("Pill media dimensions (400x136, r=32)", medW == 400.0 && medH == 136.0 && medR == 32.0);

        var (privW, _, _) = IslandLogic.GetPillDimensions("Idle", false, true);
        AssertTest("Pill privacy widening (+24px)", privW == 214.0);

        var (timerW, _, _) = IslandLogic.GetPillDimensions("Timer", false, false);
        AssertTest("Pill timer idle dimensions (118px)", timerW == 118.0);

        var (timerPlayW, _, _) = IslandLogic.GetPillDimensions("Timer", true, false);
        AssertTest("Pill timer playing dimensions (148px)", timerPlayW == 148.0);

        // Precise pill re-animation checks (verifies bugfix for privacy widening collision)
        AssertTest("Timer reanimates when music starts (118 -> 148)",
            IslandLogic.ShouldReanimateTimerPill(118.0, isPlaying: true, privacyActive: false));
        AssertTest("Timer does not reanimate when width matches (118 == 118)",
            !IslandLogic.ShouldReanimateTimerPill(118.0, isPlaying: false, privacyActive: false));
        AssertTest("Timer with privacy does not trigger false reanimation (142 == 142)",
            !IslandLogic.ShouldReanimateTimerPill(142.0, isPlaying: false, privacyActive: true));
        AssertTest("Timer with privacy reanimates when playing changes (142 -> 172)",
            IslandLogic.ShouldReanimateTimerPill(142.0, isPlaying: true, privacyActive: true));

        var rectTop = IslandLogic.CalculateIslandRect(440, 190, 34, false, 6);
        AssertTest("Pill top centering (x=125, y=6, w=190, h=34)",
            rectTop.X == 125.0 && rectTop.Y == 6.0 && rectTop.Width == 190.0 && rectTop.Height == 34.0);

        var rectBottom = IslandLogic.CalculateIslandRect(440, 190, 34, true, 6);
        AssertTest("Pill bottom docking (y=120)", rectBottom.Y == 120.0);

        // 4. Notification Filtering & Parsing
        AssertTest("Junk filter empty text", IslandLogic.IsJunkNotificationText(""));
        AssertTest("Junk filter whitespace text", IslandLogic.IsJunkNotificationText("   \t\n"));
        AssertTest("Junk filter 'Dismiss'", IslandLogic.IsJunkNotificationText("Dismiss"));
        AssertTest("Junk filter 'Maximize'", IslandLogic.IsJunkNotificationText("Maximize window"));
        AssertTest("Valid notification text allowed", !IslandLogic.IsJunkNotificationText("Alice: Hey, are you free?"));

        var (pApp, pTitle, pBody) = IslandLogic.ParseNotification("", new[] { "Telegram", "Bob", "Hello there!" });
        AssertTest("Telegram notification parsing", pApp == "Telegram" && pTitle == "Bob" && pBody == "Hello there!");

        var (wApp, wTitle, wBody) = IslandLogic.ParseNotification("", new[] { "WhatsApp", "Team Group", "Meeting at 4" });
        AssertTest("WhatsApp notification parsing", wApp == "WhatsApp" && wTitle == "Team Group" && wBody == "Meeting at 4");

        var (dApp, dTitle, dBody) = IslandLogic.ParseNotification("", new[] { "Discord", "Gaming Guild", "Raid starting" });
        AssertTest("Discord notification parsing", dApp == "Discord" && dTitle == "Gaming Guild" && dBody == "Raid starting");

        var (gApp, gTitle, gBody) = IslandLogic.ParseNotification("Slack", new[] { "Project Channel", "Build succeeded" });
        AssertTest("Generic notification parsing", gApp == "Slack" && gTitle == "Project Channel" && gBody == "Build succeeded");

        // Badge styling tests
        var tgBadge = IslandLogic.GetBadgeStyle("Telegram", "Bob");
        AssertTest("Telegram badge color and icon", tgBadge.R == 42 && tgBadge.G == 171 && tgBadge.B == 238 && tgBadge.Glyph == "✈");

        var waBadge = IslandLogic.GetBadgeStyle("WhatsApp", "Mom");
        AssertTest("WhatsApp badge color and icon", waBadge.R == 37 && waBadge.G == 211 && waBadge.B == 102 && waBadge.Glyph == "💬");

        var discBadge = IslandLogic.GetBadgeStyle("Discord", "Server");
        AssertTest("Discord badge color and icon", discBadge.R == 88 && discBadge.G == 101 && discBadge.B == 242 && discBadge.Glyph == "🎮");

        var timerBadge = IslandLogic.GetBadgeStyle("Timer", "Focus complete");
        AssertTest("Timer badge color and icon", timerBadge.R == 255 && timerBadge.G == 159 && timerBadge.B == 10 && timerBadge.Glyph == "\uE916");

        var defaultBadge = IslandLogic.GetBadgeStyle("", "GitHub");
        AssertTest("Default badge icon initial letter", defaultBadge.Glyph == "G");

        // 5. Expanded Views & Wheel Navigation Tests
        AssertTest("Initial view when idle is Clock (0)",
            IslandLogic.GetInitialExpandedView(isPlaying: false) == (int)IslandLogic.ExpandedView.Clock);
        AssertTest("Initial view when music is paused is Clock (0)",
            IslandLogic.GetInitialExpandedView(isPlaying: false) == (int)IslandLogic.ExpandedView.Clock);
        AssertTest("Initial view when music is playing is Music (1)",
            IslandLogic.GetInitialExpandedView(isPlaying: true) == (int)IslandLogic.ExpandedView.Music);
        AssertTest("Scroll down from Clock (0) goes to Music (1)",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Clock, -120) == (int)IslandLogic.ExpandedView.Music);
        AssertTest("Scroll down from Music (1) goes to Timer (2)",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Music, -120) == (int)IslandLogic.ExpandedView.Timer);
        AssertTest("Scroll down from Timer (2) goes to Clipboard (3) [3rd scroll]",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Timer, -120) == (int)IslandLogic.ExpandedView.Clipboard);
        AssertTest("Scroll down from Clipboard (3) stays at Clipboard (3) [clamped]",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Clipboard, -120) == (int)IslandLogic.ExpandedView.Clipboard);
        AssertTest("Scroll up from Clipboard (3) goes to Timer (2)",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Clipboard, 120) == (int)IslandLogic.ExpandedView.Timer);
        AssertTest("Scroll up from Timer (2) goes to Music (1)",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Timer, 120) == (int)IslandLogic.ExpandedView.Music);
        AssertTest("Scroll up from Music (1) goes to Clock (0)",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Music, 120) == (int)IslandLogic.ExpandedView.Clock);
        AssertTest("Scroll up from Clock (0) stays at Clock (0) [clamped]",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Clock, 120) == (int)IslandLogic.ExpandedView.Clock);
        AssertTest("Zero scroll delta does not change view",
            IslandLogic.NextView((int)IslandLogic.ExpandedView.Music, 0) == (int)IslandLogic.ExpandedView.Music);

        // 6. Clipboard Preview Formatting Tests
        AssertTest("Clipboard format empty text",
            IslandLogic.FormatClipboardPreview("") == "Clipboard is empty");
        AssertTest("Clipboard format whitespace text",
            IslandLogic.FormatClipboardPreview("   \t\n  ") == "Clipboard is empty");
        AssertTest("Clipboard format multi-line collapsing",
            IslandLogic.FormatClipboardPreview("Hello\r\nWorld\tTest") == "Hello World Test");
        AssertTest("Clipboard format long text truncation",
            IslandLogic.FormatClipboardPreview(new string('A', 100), 10) == "AAAAAAAAAA…");

        // 7. NativeMethods & Interop Helpers
        AssertTest("Cached process name lookup returns empty on pid 0", NativeMethods.GetProcessNameCached(0) == "");
        var rc = new NativeMethods.RECT { Left = 10, Top = 20, Right = 110, Bottom = 80 };
        AssertTest("NativeMethods.RECT width calculation", rc.Width == 100);
        AssertTest("NativeMethods.RECT height calculation", rc.Height == 60);

        // 8. Domain Services (including ClipboardService)
        var settings = new SettingsService();
        AssertTest("SettingsService initializes without error", settings != null);

        string? resolvedExe = settings.GetApplicationExecutablePath();
        AssertTest("SettingsService resolves valid application executable path",
            !string.IsNullOrEmpty(resolvedExe) && resolvedExe.EndsWith("DynamicIsland.exe", StringComparison.OrdinalIgnoreCase));
        AssertTest("SettingsService resolves non-empty startup shortcut path",
            !string.IsNullOrEmpty(SettingsService.GetStartupShortcutPath()) && SettingsService.GetStartupShortcutPath().EndsWith(".lnk"));
        AssertTest("SettingsService resolves non-empty startup cmd path",
            !string.IsNullOrEmpty(SettingsService.GetStartupCmdPath()) && SettingsService.GetStartupCmdPath().EndsWith(".cmd"));

        var focusTimer = new FocusTimerService();
        AssertTest("FocusTimerService initially inactive", !focusTimer.IsActive);
        focusTimer.Start(25);
        AssertTest("FocusTimerService active after start", focusTimer.IsActive && focusTimer.Duration.TotalMinutes == 25);
        focusTimer.Cancel();
        AssertTest("FocusTimerService inactive after cancel", !focusTimer.IsActive);
        focusTimer.Dispose();

        var clipService = new ClipboardService();
        AssertTest("ClipboardService starts with 0 items", clipService.Count == 0);
        AssertTest("ClipboardService rejects empty string", !clipService.AddText(""));
        AssertTest("ClipboardService rejects whitespace", !clipService.AddText("   "));

        clipService.AddText("First copied item");
        AssertTest("ClipboardService count 1 after first add", clipService.Count == 1);
        AssertTest("ClipboardService latest text is First item", clipService.LatestText == "First copied item");

        clipService.AddText("Second copied item");
        AssertTest("ClipboardService count 2 after second add", clipService.Count == 2);
        AssertTest("ClipboardService latest text is Second item", clipService.LatestText == "Second copied item");

        // MRU duplicate promotion test
        clipService.AddText("First copied item");
        AssertTest("Re-adding First item moves it to top without duplicates",
            clipService.Count == 2 && clipService.LatestText == "First copied item");

        // Cap at 10 items test
        for (int i = 3; i <= 15; i++)
        {
            clipService.AddText($"Copied item #{i}");
        }
        AssertTest("ClipboardService caps strictly at 10 items", clipService.Count == 10);
        AssertTest("ClipboardService latest is #15", clipService.LatestText == "Copied item #15");
        AssertTest("ClipboardService history has exactly 10 items", clipService.History.Count == 10);

        // Clear test
        clipService.Clear();
        AssertTest("ClipboardService clear empties history", clipService.Count == 0 && clipService.LatestText == "");
        clipService.Dispose();

        // 9. Timer Dialog Calculations & Bounds
        AssertTest("Timer from ratio 0.0 -> Min (1)", IslandLogic.CalculateTimerFromRatio(0.0, 1, 180) == 1);
        AssertTest("Timer from ratio 1.0 -> Max (180)", IslandLogic.CalculateTimerFromRatio(1.0, 1, 180) == 180);
        AssertTest("Timer from ratio negative clamped to Min", IslandLogic.CalculateTimerFromRatio(-0.5, 1, 180) == 1);
        AssertTest("Timer from ratio >1.0 clamped to Max", IslandLogic.CalculateTimerFromRatio(1.5, 1, 180) == 180);
        AssertTest("Timer from ratio 0.5 maps midway rounded (90)", IslandLogic.CalculateTimerFromRatio(0.5, 1, 180) == 90);
        AssertTest("Timer from ratio 0.5 maps midway exact (50)", IslandLogic.CalculateTimerFromRatio(0.5, 0, 100) == 50);

        AssertTest("Timer fill width at min is 0", IslandLogic.CalculateTimerFillWidth(300, 1, 1, 180) == 0.0);
        AssertTest("Timer fill width at max is full track (300)", IslandLogic.CalculateTimerFillWidth(300, 180, 1, 180) == 300.0);
        AssertTest("Timer fill width invalid track width (0)", IslandLogic.CalculateTimerFillWidth(0, 50, 1, 180) == 0.0);
        AssertTest("Timer fill width negative track width (0)", IslandLogic.CalculateTimerFillWidth(-50, 50, 1, 180) == 0.0);

        var testTime = new DateTime(2026, 10, 8, 12, 0, 0);
        AssertTest("Timer ends at formatted correctly", IslandLogic.FormatTimerEndsAt(testTime, 25).Contains("12:25"));

        // 10. Charging Pill, Power Detection & Battery Math Tests
        var (chgW, chgH, chgR) = IslandLogic.GetPillDimensions("Charging", false, false);
        AssertTest("Pill charging dimensions (260x34, r=17)", chgW == 260.0 && chgH == 34.0 && chgR == 17.0);

        var (chgPrivW, _, _) = IslandLogic.GetPillDimensions("Charging", false, true);
        AssertTest("Pill charging with privacy widening (+24px -> 284)", chgPrivW == 284.0);

        var (idleChgW, idleChgH, idleChgR) = IslandLogic.GetPillDimensions("Idle", false, false, isCharging: true);
        AssertTest("Pill idle charging dimensions (+26px -> 216x34, r=17)", idleChgW == 216.0 && idleChgH == 34.0 && idleChgR == 17.0);

        var (idleChgPrivW, _, _) = IslandLogic.GetPillDimensions("Idle", false, true, isCharging: true);
        AssertTest("Pill idle charging with privacy widening (+50px -> 240)", idleChgPrivW == 240.0);

        AssertTest("Battery format normal (85 -> 85%)", IslandLogic.FormatBatteryPercent(85) == "85%");
        AssertTest("Battery format zero (0 -> 0%)", IslandLogic.FormatBatteryPercent(0) == "0%");
        AssertTest("Battery format full (100 -> 100%)", IslandLogic.FormatBatteryPercent(100) == "100%");
        AssertTest("Battery format negative clamped (-10 -> 0%)", IslandLogic.FormatBatteryPercent(-10) == "0%");
        AssertTest("Battery format overflow clamped (125 -> 100%)", IslandLogic.FormatBatteryPercent(125) == "100%");

        AssertTest("Battery fill width 100% full (22.0)", IslandLogic.CalculateBatteryFillWidth(22.0, 100) == 22.0);
        AssertTest("Battery fill width 50% half (11.0)", IslandLogic.CalculateBatteryFillWidth(22.0, 50) == 11.0);
        AssertTest("Battery fill width 0% empty (0.0)", IslandLogic.CalculateBatteryFillWidth(22.0, 0) == 0.0);
        AssertTest("Battery fill width negative clamped (0.0)", IslandLogic.CalculateBatteryFillWidth(22.0, -15) == 0.0);
        AssertTest("Battery fill width >100% clamped (22.0)", IslandLogic.CalculateBatteryFillWidth(22.0, 150) == 22.0);
        AssertTest("Battery fill width invalid track (0.0)", IslandLogic.CalculateBatteryFillWidth(0.0, 80) == 0.0);
        AssertTest("Battery fill width negative track (0.0)", IslandLogic.CalculateBatteryFillWidth(-20.0, 80) == 0.0);
        AssertTest("Battery fill width idle interior track (18.0, 85%)", IslandLogic.CalculateBatteryFillWidth(18.0, 85) == 15.3);

        AssertTest("Charger plugged in offline -> online", IslandLogic.IsChargerPluggedIn(0, 1));
        AssertTest("Charger plugged in unknown -> online", IslandLogic.IsChargerPluggedIn(255, 1));
        AssertTest("Charger already online -> online (no re-trigger)", !IslandLogic.IsChargerPluggedIn(1, 1));
        AssertTest("Charger unplugged online -> offline (no connect trigger)", !IslandLogic.IsChargerPluggedIn(1, 0));
        AssertTest("Charger still offline 0 -> 0", !IslandLogic.IsChargerPluggedIn(0, 0));

        var powerService = new PowerService();
        AssertTest("PowerService instantiated cleanly", powerService != null);
        int simulatedPct = -1;
        bool statusChangedFired = false;
        bool statusIsCharging = false;
        powerService!.ChargerConnected += pct => simulatedPct = pct;
        powerService.PowerStatusChanged += (chg, pct) =>
        {
            statusChangedFired = true;
            statusIsCharging = chg;
        };

        powerService.SimulateChargerConnected(92);
        AssertTest("PowerService simulation triggers ChargerConnected event (92%)", simulatedPct == 92);
        AssertTest("PowerService simulation triggers PowerStatusChanged event (online)", statusChangedFired && statusIsCharging);
        AssertTest("PowerService IsCharging property true", powerService.IsCharging);
        AssertTest("PowerService BatteryPercent property 92", powerService.BatteryPercent == 92);

        powerService.SimulateChargerConnected(150);
        AssertTest("PowerService simulation clamps upper percent (100%)", simulatedPct == 100);
        powerService.SimulateChargerConnected(-20);
        AssertTest("PowerService simulation clamps lower percent (0%)", simulatedPct == 0);

        powerService.SimulateChargerDisconnected();
        AssertTest("PowerService SimulateChargerDisconnected sets IsCharging false", !powerService.IsCharging);
        AssertTest("PowerService SimulateChargerDisconnected fired PowerStatusChanged (offline)", !statusIsCharging);

        powerService.Dispose();
        AssertTest("PowerService disposes cleanly", true);

        // 11. PrivacyService — IsEntryInUse logic (regression for missing-stop-value bug)
        // Device actively in use: start > 0, stop MISSING (Windows hasn't written it yet)
        AssertTest("Privacy: in use when start > 0 and stop is null (stop value absent)",
            PrivacyService.IsEntryInUse(start: 134358181803476383L, stop: null));

        // Device actively in use: start > 0, stop == 0 (older Windows sentinel)
        AssertTest("Privacy: in use when start > 0 and stop == 0 (older sentinel)",
            PrivacyService.IsEntryInUse(start: 134358181803476383L, stop: 0L));

        // Device released: start > 0, stop > 0 (session ended)
        AssertTest("Privacy: NOT in use when stop > 0 (session ended)",
            !PrivacyService.IsEntryInUse(start: 134358181803476383L, stop: 134358182068634324L));

        // No session at all: both missing
        AssertTest("Privacy: NOT in use when both start and stop are null",
            !PrivacyService.IsEntryInUse(start: null, stop: null));

        // No session at all: start == 0, stop missing (never used)
        AssertTest("Privacy: NOT in use when start == 0 (never accessed device)",
            !PrivacyService.IsEntryInUse(start: 0L, stop: null));

        // Paranoid: start missing, stop missing — not in use
        AssertTest("Privacy: NOT in use when start is null regardless of stop",
            !PrivacyService.IsEntryInUse(start: null, stop: 0L));

        // Edge: start missing, stop > 0 — not in use
        AssertTest("Privacy: NOT in use when start is null and stop > 0",
            !PrivacyService.IsEntryInUse(start: null, stop: 134358182068634324L));

        // Device actively in use: start > stop (new session started after previous stop timestamp)
        AssertTest("Privacy: in use when start > stop (new session started after prior stop)",
            PrivacyService.IsEntryInUse(start: 200L, stop: 100L));

        // Device released: stop >= start (session ended)
        AssertTest("Privacy: NOT in use when stop >= start",
            !PrivacyService.IsEntryInUse(start: 100L, stop: 100L));

        // Previously broken: start > 0 and stop completely absent — THIS WAS THE BUG
        AssertTest("Privacy: (regression) start > 0, stop absent => dot should show [was broken]",
            PrivacyService.IsEntryInUse(start: 100L, stop: null));

        // Live capture test
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
            Console.WriteLine($"\n[CAPTURE DEVICES DUMP] Found {endpoints.Count} active capture endpoints:");
            for (int d = 0; d < endpoints.Count; d++)
            {
                var device = endpoints[d];
                try
                {
                    Console.WriteLine($"  Device [{d}]: {device.FriendlyName}");
                    var sm = device.AudioSessionManager;
                    if (sm != null)
                    {
                        var sessions = sm.Sessions;
                        Console.WriteLine($"    Sessions count: {sessions.Count}");
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            uint pid = s.GetProcessID;
                            string procName = "unknown";
                            try { if (pid > 0) procName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
                            Console.WriteLine($"      Session [{i}]: PID={pid} ({procName}), State={s.State}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    Device [{d}] Error: {ex.Message}");
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NOTE] Capture dump failed: {ex.Message}");
        }

        Console.WriteLine($"\nResults: {_passed} passed, {_failed} failed out of {_passed + _failed} tests.\n");
        return _failed == 0 ? 0 : 1;
    }
}
