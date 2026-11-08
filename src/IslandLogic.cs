// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace DynamicIsland;

/// <summary>
/// Ultra-lightweight, zero-allocation UI logic, badges, and mathematical transformations.
/// Completely testable without UI hardware.
/// </summary>
public static class IslandLogic
{
    private const string Mute = "\uE74F", Speaker = "\uE767";   // Segoe Fluent Icons
    public const string GlyphPlay = "\uE768";
    public const string GlyphPause = "\uE769";

    /// <summary>Returns Pause glyph (E769) when playing, Play glyph (E768) when paused.</summary>
    public static string PlayPauseGlyph(bool isPlaying) => isPlaying ? GlyphPause : GlyphPlay;

    /// <summary>
    /// Returns the optical margin thickness (left offset) for play vs pause glyph centering.
    /// Play triangle has visual weight to the left so +1px right aligns its center of mass.
    /// Pause double-bars are symmetric and dead-centered (0px offset).
    /// </summary>
    public static Thickness PlayPauseOpticalMargin(bool isPlaying) =>
        isPlaying ? new Thickness(0) : new Thickness(1, 0, 0, 0);

    /// <summary>
    /// Returns the exact button diameter and corner radius for the circular Play/Pause button.
    /// Guarantees 1:1 circle geometry (radius = size / 2) to eliminate layout clipping and deformed edges.
    /// </summary>
    public static (double size, double cornerRadius) PlayPauseButtonDimensions() => (36.0, 18.0);

    public static string VolumeGlyph(float volume, bool muted) =>
        muted || float.IsNaN(volume) || volume <= 0f ? Mute : Speaker;

    /// <summary>0-100 for the HUD; a muted or invalid device reads as 0.</summary>
    public static int Percent(float volume, bool muted)
    {
        if (muted || float.IsNaN(volume) || float.IsInfinity(volume) || volume <= 0f) return 0;
        return Math.Clamp((int)Math.Round(volume * 100f), 0, 100);
    }

    public enum ExpandedView
    {
        Clock = 0,
        Music = 1,
        Timer = 2,
        Clipboard = 3
    }

    /// <summary>Returns the default expanded view: Music if media is actively playing, Clock otherwise (idle).</summary>
    public static int GetInitialExpandedView(bool isPlaying) =>
        isPlaying ? (int)ExpandedView.Music : (int)ExpandedView.Clock;

    /// <summary>Calculates the next expanded view based on wheel scroll delta (clamped between 0 and 3).</summary>
    public static int NextView(int currentView, int scrollDelta)
    {
        int step = scrollDelta < 0 ? 1 : (scrollDelta > 0 ? -1 : 0);
        return Math.Clamp(currentView + step, 0, 3);
    }

    /// <summary>Formats clipboard text into a clean compact preview, collapsing excess whitespace and newlines.</summary>
    public static string FormatClipboardPreview(string? text, int maxChars = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Clipboard is empty";
        ReadOnlySpan<char> span = text.AsSpan().Trim();
        var sb = new StringBuilder(Math.Min(span.Length, maxChars + 3));
        bool inWhitespace = false;
        int i = 0;
        for (; i < span.Length && sb.Length < maxChars; i++)
        {
            char c = span[i];
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                inWhitespace = false;
            }
        }

        if (i < span.Length) sb.Append('…');
        return sb.ToString();
    }

    /// <summary>Formats remaining countdown duration cleanly.</summary>
    public static string FormatTimer(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "00:00";
        return left.TotalHours >= 1
            ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
            : $"{left.Minutes:00}:{left.Seconds:00}";
    }

    /// <summary>Calculates pill dimensions based on mode, playing status, privacy indicators, and charging status.</summary>
    public static (double width, double height, double radius) GetPillDimensions(string mode, bool isPlaying, bool privacyActive, bool isCharging = false)
    {
        double privacyExtra = privacyActive ? (mode == "Media" ? 0.0 : 24.0) : 0.0;
        double chargingExtra = (isCharging && mode == "Idle") ? 26.0 : 0.0;
        double baseW = mode switch
        {
            "Media"   => 400.0,
            "Compact" => 250.0,
            "Volume"  => 280.0,
            "Notify"  => 360.0,
            "Timer"   => (isPlaying ? 148.0 : 118.0),
            "Charging"=> 260.0,
            "PinTarget"=> 280.0,
            _         => 190.0,
        };
        double w = baseW + privacyExtra + chargingExtra;
        double h = mode switch
        {
            "Media"  => 136.0,
            "Volume" => 44.0,
            "Notify" => 70.0,
            _        => 34.0,
        };
        double r = mode switch
        {
            "Media"  => 32.0,
            "Volume" => 22.0,
            "Notify" => 35.0,
            _        => 17.0,
        };
        return (w, h, r);
    }

    /// <summary>
    /// Accurately checks whether the Timer pill should morph its geometry when playback or privacy states change.
    /// Replaces the heuristic threshold that previously collided with privacy widening.
    /// </summary>
    public static bool ShouldReanimateTimerPill(double currentWidth, bool isPlaying, bool privacyActive)
    {
        var (targetWidth, _, _) = GetPillDimensions("Timer", isPlaying, privacyActive);
        return Math.Abs(currentWidth - targetWidth) > 1.0;
    }

    /// <summary>Calculates the absolute target bounding rectangle for morphing geometry.</summary>
    public static Rect CalculateIslandRect(double winW, double w, double h, bool bottom, double topGap = 6)
    {
        double x = (winW - w) / 2.0;
        double y = bottom ? 160.0 - topGap - h : topGap;
        return new Rect(x, y, w, h);
    }

    /// <summary>Zero-allocation filter for noise/junk notification strings.</summary>
    public static bool IsJunkNotificationText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        ReadOnlySpan<char> span = text.AsSpan().Trim();

        return text.Contains("maximize", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("minimize", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("bookmark this", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("has access to this site", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("wants access to this site", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("extensions control", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("systemsettings", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("windows shell experience host", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("settings", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("new notification", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("action center", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("dismiss", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("close", StringComparison.OrdinalIgnoreCase) ||
               span.Equals("reply", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinFrom(IReadOnlyList<string> texts, int startIndex)
    {
        if (texts.Count <= startIndex) return "";
        if (texts.Count == startIndex + 1) return texts[startIndex];
        var sb = new StringBuilder(texts[startIndex]);
        for (int i = startIndex + 1; i < texts.Count; i++)
        {
            sb.Append(' ').Append(texts[i]);
        }
        return sb.ToString();
    }

    /// <summary>Parses notification sender and message body from structured toast texts.</summary>
    public static (string app, string title, string body) ParseNotification(string? defaultApp, IReadOnlyList<string>? texts)
    {
        if (texts == null || texts.Count == 0) return (defaultApp ?? "", "", "");

        string app = defaultApp ?? "";
        string title;
        string body;

        if (texts[0].Equals("Telegram", StringComparison.OrdinalIgnoreCase) ||
            texts[0].Equals("Telegram Desktop", StringComparison.OrdinalIgnoreCase))
        {
            app = "Telegram";
            title = texts.Count > 1 ? texts[1] : "Telegram";
            body = JoinFrom(texts, 2);
        }
        else if (texts[0].Equals("WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            app = "WhatsApp";
            title = texts.Count > 1 ? texts[1] : "WhatsApp";
            body = JoinFrom(texts, 2);
        }
        else if (texts[0].Equals("Discord", StringComparison.OrdinalIgnoreCase))
        {
            app = "Discord";
            title = texts.Count > 1 ? texts[1] : "Discord";
            body = JoinFrom(texts, 2);
        }
        else
        {
            title = texts[0];
            body = JoinFrom(texts, 1);
        }

        return (app, title, body);
    }

    /// <summary>
    /// Pure, zero-allocation badge styling resolver for toast notifications (RGB and glyph).
    /// </summary>
    public static (byte R, byte G, byte B, string Glyph) GetBadgeStyle(string? app, string title)
    {
        bool Matches(string keyword) =>
            (!string.IsNullOrEmpty(app) && app.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(title) && title.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (Matches("pinned") || Matches("topmost"))
            return (255, 179, 0, "📌"); // Pin amber
        if (Matches("voice") || Matches("nixi") || Matches("assistant"))
            return (56, 130, 246, "\uE720"); // Voice blue with microphone glyph
        if (Matches("chrome"))
            return (66, 133, 244, "🌐"); // Chrome Google blue
        if (Matches("edge") || Matches("msedge"))
            return (0, 120, 215, "🌐"); // Edge blue
        if (Matches("firefox"))
            return (255, 113, 36, "🦊"); // Firefox orange
        if (Matches("timer") || Matches("focus session"))
            return (255, 159, 10, "\uE916"); // Focus timer orange
        if (Matches("telegram"))
            return (42, 171, 238, "✈"); // Telegram blue
        if (Matches("whatsapp"))
            return (37, 211, 102, "💬"); // WhatsApp green
        if (Matches("discord"))
            return (88, 101, 242, "🎮"); // Discord blurple
        if (Matches("spotify"))
            return (29, 185, 84, "🎵"); // Spotify green
        if (Matches("slack"))
            return (224, 30, 90, "#"); // Slack magenta
        if (Matches("mail") || Matches("outlook"))
            return (0, 120, 212, "✉"); // Outlook blue

        // Default neutral badge
        string icon = title.Length > 0 ? title[..1].ToUpperInvariant() : "!";
        return (58, 58, 60, icon);
    }

    /// <summary>Calculates integer minutes clamped between min and max from a normalized slider ratio [0.0, 1.0].</summary>
    public static int CalculateTimerFromRatio(double ratio, int min = 1, int max = 180)
    {
        if (double.IsNaN(ratio)) ratio = 0.0;
        double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);
        return Math.Clamp((int)Math.Round(min + clampedRatio * (max - min)), min, max);
    }

    /// <summary>Calculates target track fill width from chosen minutes, track width, and range.</summary>
    public static double CalculateTimerFillWidth(double trackWidth, int minutes, int min = 1, int max = 180)
    {
        if (trackWidth <= 0.0 || max <= min) return 0.0;
        double clampedMin = Math.Clamp(minutes, min, max);
        return Math.Clamp(trackWidth * (clampedMin - min) / (max - min), 0.0, trackWidth);
    }

    /// <summary>Formats the estimated end time string cleanly.</summary>
    public static string FormatTimerEndsAt(DateTime now, int minutes) =>
        $"Ends at {now.AddMinutes(Math.Max(0, minutes)):t}";

    /// <summary>Formats battery percentage string clamped between 0 and 100.</summary>
    public static string FormatBatteryPercent(int percent) =>
        $"{Math.Clamp(percent, 0, 100)}%";

    /// <summary>Calculates battery fill rectangle width clamped to track bounds.</summary>
    public static double CalculateBatteryFillWidth(double maxFillWidth, int percent)
    {
        if (maxFillWidth <= 0) return 0.0;
        double ratio = Math.Clamp(percent, 0, 100) / 100.0;
        return Math.Round(maxFillWidth * ratio, 1);
    }

    /// <summary>Checks whether power state transitioned to plugged-in online AC state.</summary>
    public static bool IsChargerPluggedIn(byte previousAcStatus, byte currentAcStatus) =>
        previousAcStatus != 1 && currentAcStatus == 1;

    /// <summary>
    /// Pure state-machine transition resolver for notification dismissal.
    /// Determines target mode and preserves or restores expanded view tab when a notification
    /// expires or is dismissed by user click.
    /// </summary>
    public static (string TargetMode, int TargetView) DetermineNotificationDismissTarget(
        bool isHovered,
        string preNotifyMode,
        int preNotifyView,
        bool isPlaying,
        bool isTimerActive)
    {
        int targetView = preNotifyView;

        // If user is currently hovering the island, stay expanded in Media mode on their prior view
        if (isHovered)
        {
            return ("Media", targetView);
        }

        // If island was previously in expanded Media mode but mouse is no longer hovering, return to rest
        if (preNotifyMode == "Media")
        {
            string restMode = isTimerActive ? "Timer" : (isPlaying ? "Compact" : "Idle");
            return (restMode, targetView);
        }

        // If island was previously in a resting mode (Idle, Compact, Timer), return to that exact mode
        if (preNotifyMode is "Idle" or "Compact" or "Timer")
        {
            return (preNotifyMode, targetView);
        }

        // Fallback to active resting state
        string fallback = isTimerActive ? "Timer" : (isPlaying ? "Compact" : "Idle");
        return (fallback, targetView);
    }

    /// <summary>Formats playback timestamp as m:ss or h:mm:ss with leading zero padding on seconds.</summary>
    public static string FormatPlaybackTime(TimeSpan time)
    {
        if (time <= TimeSpan.Zero) return "0:00";
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    /// <summary>Calculates normalized progress ratio [0.0, 1.0] from current position and total duration.</summary>
    public static double CalculateProgressRatio(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return 0.0;
        double ratio = (double)position.Ticks / duration.Ticks;
        return double.IsNaN(ratio) || double.IsInfinity(ratio) ? 0.0 : Math.Clamp(ratio, 0.0, 1.0);
    }

    /// <summary>Calculates progress bar fill pixel width clamped to track bounds.</summary>
    public static double CalculateProgressWidth(double ratio, double trackWidth)
    {
        if (trackWidth <= 0.0 || double.IsNaN(ratio) || double.IsNaN(trackWidth)) return 0.0;
        return Math.Round(Math.Clamp(ratio, 0.0, 1.0) * trackWidth, 1);
    }

    /// <summary>Calculates target seek TimeSpan from click position on track.</summary>
    public static TimeSpan CalculateScrubTime(double clickX, double trackWidth, TimeSpan duration)
    {
        if (trackWidth <= 0.0 || duration <= TimeSpan.Zero) return TimeSpan.Zero;
        double ratio = Math.Clamp(clickX / trackWidth, 0.0, 1.0);
        return TimeSpan.FromTicks((long)(duration.Ticks * ratio));
    }

    /// <summary>Cleans and formats audio endpoint device name for compact pill display.</summary>
    public static string FormatAudioDeviceName(string? rawDeviceName, int maxChars = 14)
    {
        if (string.IsNullOrWhiteSpace(rawDeviceName)) return "Living Room";
        string clean = rawDeviceName.Trim();

        // Strip known audio driver suffixes like "(Realtek(R) Audio)", "(High Definition Audio Device)"
        int parenIdx = clean.IndexOf('(');
        if (parenIdx > 0)
        {
            string prefix = clean[..parenIdx].Trim();
            if (prefix.Length >= 3) clean = prefix;
        }

        if (clean.Length > maxChars)
        {
            clean = clean[..(maxChars - 1)].TrimEnd() + "…";
        }
        return clean;
    }

    /// <summary>Calculates normalized volume ratio [0.0, 1.0] from click X and track width.</summary>
    public static float CalculateVolumeRatio(double clickX, double trackWidth)
    {
        if (trackWidth <= 0.0 || double.IsNaN(clickX) || double.IsNaN(trackWidth)) return 0.0f;
        return (float)Math.Clamp(clickX / trackWidth, 0.0, 1.0);
    }

    /// <summary>Calculates volume track fill width in pixels clamped to track bounds.</summary>
    public static double CalculateVolumeFillWidth(float volume, double trackWidth)
    {
        if (trackWidth <= 0.0 || float.IsNaN(volume)) return 0.0;
        return Math.Round(Math.Clamp(volume, 0.0f, 1.0f) * trackWidth, 1);
    }

    /// <summary>Calculates volume slider thumb left offset so it centers on the fill tip without clipping.</summary>
    public static double CalculateVolumeThumbLeft(float volume, double trackWidth, double thumbWidth = 10.0)
    {
        if (trackWidth <= 0.0 || thumbWidth <= 0.0 || float.IsNaN(volume)) return 0.0;
        double maxLeft = Math.Max(0.0, trackWidth - thumbWidth);
        double centerOffset = (Math.Clamp(volume, 0.0f, 1.0f) * trackWidth) - (thumbWidth / 2.0);
        return Math.Round(Math.Clamp(centerOffset, 0.0, maxLeft), 1);
    }

    /// <summary>
    /// Checks whether a click position (clickX, clickY) within the window falls strictly inside the designated
    /// center drag zone of the island, preventing dragging when clicking playback controls, volume controls, or progress scrubbers.
    /// </summary>
    public static bool IsCenterDragZone(double clickX, double clickY, Rect islandRect, string mode, double maxCenterHalfWidth = 38.0)
    {
        if (islandRect.Width <= 0.0 || islandRect.Height <= 0.0) return false;
        if (double.IsNaN(clickX) || double.IsNaN(clickY)) return false;

        // Must be inside the island's visible boundary
        if (clickX < islandRect.X || clickX > islandRect.X + islandRect.Width ||
            clickY < islandRect.Y || clickY > islandRect.Y + islandRect.Height)
        {
            return false;
        }

        double centerX = islandRect.X + (islandRect.Width / 2.0);
        double dx = Math.Abs(clickX - centerX);

        // In Media mode, the center drag zone is bounded between the left playback controls
        // (ending ~X=192) and right volume controls (starting ~X=276).
        if (string.Equals(mode, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return dx <= maxCenterHalfWidth;
        }

        // In Idle / Compact / other modes, center zone is also within maxCenterHalfWidth
        // (or at most 50% of the island's total width).
        double effectiveHalfWidth = Math.Min(maxCenterHalfWidth, islandRect.Width * 0.25);
        return dx <= effectiveHalfWidth;
    }

    /// <summary>
    /// Checks whether a window rectangle fully covers or exceeds a screen/monitor rectangle (with tolerance),
    /// indicating an application running in full-screen mode (e.g. video, browser F11, game).
    /// </summary>
    public static bool IsRectFullScreen(double windowLeft, double windowTop, double windowRight, double windowBottom,
                                       double screenLeft, double screenTop, double screenRight, double screenBottom,
                                       double tolerance = 2.0)
    {
        if (windowRight <= windowLeft || windowBottom <= windowTop) return false;
        if (screenRight <= screenLeft || screenBottom <= screenTop) return false;

        return windowLeft <= screenLeft + tolerance &&
               windowTop <= screenTop + tolerance &&
               windowRight >= screenRight - tolerance &&
               windowBottom >= screenBottom - tolerance;
    }

    /// <summary>
    /// Pure hit-test determining whether a screen coordinate (e.g. mouse release point from an external app drag)
    /// falls on or within a generous cushion around the Dynamic Island pill.
    /// </summary>
    public static bool IsDropOnIsland(double cursorX, double cursorY, double left, double top, double right, double bottom, double padding = 16.0)
    {
        if (double.IsNaN(cursorX) || double.IsNaN(cursorY)) return false;
        if (right <= left || bottom <= top) return false;

        return cursorX >= left - padding &&
               cursorX <= right + padding &&
               cursorY >= top - padding &&
               cursorY <= bottom + padding;
    }

    /// <summary>
    /// Formats a clean, user-friendly display name for a pinned application window (e.g. "Google Chrome", "Spotify", "Visual Studio Code").
    /// </summary>
    public static string FormatPinnedAppName(string? windowTitle, string? processName)
    {
        string proc = (processName ?? "").Trim().ToLowerInvariant();
        if (proc == "chrome") return "Google Chrome";
        if (proc == "msedge") return "Microsoft Edge";
        if (proc == "firefox") return "Mozilla Firefox";
        if (proc == "code") return "Visual Studio Code";
        if (proc == "devenv") return "Visual Studio";
        if (proc == "spotify") return "Spotify";
        if (proc == "notepad") return "Notepad";
        if (proc == "explorer") return "File Explorer";
        if (proc == "discord") return "Discord";
        if (proc == "slack") return "Slack";
        if (proc == "teams") return "Microsoft Teams";

        if (!string.IsNullOrWhiteSpace(proc))
        {
            return char.ToUpperInvariant(proc[0]) + proc[1..];
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            string title = windowTitle.Trim();
            int lastDash = title.LastIndexOf('-');
            if (lastDash >= 0 && lastDash < title.Length - 1)
            {
                string suffix = title[(lastDash + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(suffix) && suffix.Length < 30)
                {
                    return suffix;
                }
            }
            return title.Length > 24 ? title[..21] + "..." : title;
        }

        return "Application";
    }

    /// <summary>
    /// Checks whether a Win32 class name belongs to a regular pinnable application rather than desktop or shell internals.
    /// </summary>
    public static bool IsValidPinnableClassName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        string cls = className.Trim();

        if (string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cls, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats the prompt title displayed on Dynamic Island during an external app window drag.
    /// Guides the user to either drag toward the island, shake to unpin, or release to toggle the pinned topmost state.
    /// </summary>
    public static string FormatPinPromptTitle(bool isHoveringDropZone, bool isCurrentlyTopmost)
    {
        if (isCurrentlyTopmost)
        {
            return isHoveringDropZone ? "Already pinned • Shake to unpin" : "Pinned • Shake window to unpin";
        }
        return isHoveringDropZone ? "Drop to pin on top" : "Drag here to pin";
    }

    /// <summary>
    /// Returns the RGB accent color for the Pin jewel badge based on hover and topmost state.
    /// </summary>
    public static (byte R, byte G, byte B) GetPinBadgeColor(bool isHoveringDropZone, bool isCurrentlyTopmost)
    {
        if (isCurrentlyTopmost)
        {
            // Amber gold (#FFB300) when hovering, or energetic orange (#FF9F0A) - indicating already pinned
            return isHoveringDropZone ? ((byte)255, (byte)179, (byte)0) : ((byte)255, (byte)159, (byte)10);
        }
        if (isHoveringDropZone)
        {
            // Emerald green (#30D158) when ready to pin
            return ((byte)48, (byte)209, (byte)88);
        }
        // Amber gold (#FFB300) when dragging an unpinned window toward island
        return ((byte)255, (byte)179, (byte)0);
    }

    private static readonly Regex WakePhraseRegex = new(
        @"^(?:(?:hey\s+)?(?:nixie|niksi|nixi|nixy|nexi|nexus|nicky|nicki|nickie|nix|assistant|island|dynamic island|google|computer)\b[,\s]*)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CourtesyPrefixRegex = new(
        @"^(?:please\s+|can you\s+|could you\s+|would you\s+)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Strips leading wake words ("hey nixi", "hey nixie", "nixi", "hey assistant", etc.)
    /// and courtesy prefixes cleanly without mangling trailing words.
    /// </summary>
    public static string CleanVoiceCommand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string stripped = WakePhraseRegex.Replace(raw.Trim(), string.Empty).Trim();
        return CourtesyPrefixRegex.Replace(stripped, string.Empty).Trim();
    }

    /// <summary>
    /// Maps a spoken application name to its executable or URI target and display title.
    /// </summary>
    public static bool TryResolveAppTarget(string app, out string target, out string title)
    {
        if (string.IsNullOrWhiteSpace(app))
        {
            target = "";
            title = "";
            return false;
        }

        string norm = app.Trim().ToLowerInvariant();
        title = char.ToUpperInvariant(norm[0]) + norm[1..];
        string? resolved = norm switch
        {
            "spotify" or "spotifi" or "soptifi" or "opensoptifi" or "openspotify" => "spotify:",
            "chrome" or "google chrome" => "chrome.exe",
            "notepad" => "notepad.exe",
            "calculator" or "calc" => "calc.exe",
            "vs code" or "vscode" or "code" => "code",
            "terminal" or "wt" => "wt.exe",
            "cmd" or "command prompt" => "cmd.exe",
            "settings" => "ms-settings:",
            "task manager" or "taskmgr" => "taskmgr.exe",
            "file explorer" or "explorer" or "files" => "explorer.exe",
            "discord" => "discord:",
            "whatsapp" or "whats app" => "whatsapp:",
            "steam" => "steam:",
            "edge" or "microsoft edge" or "browser" => "msedge.exe",
            "brave" => "brave.exe",
            "camera" => "microsoft.windows.camera:",
            "paint" => "mspaint.exe",
            _ => null
        };

        if (resolved != null)
        {
            target = resolved;
            return true;
        }

        target = app.Trim();
        return true;
    }

    /// <summary>
    /// Deterministic sub-millisecond intent parser for voice assistant commands.
    /// </summary>
    public static VoiceIntent? ParseVoiceIntent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string clean = CleanVoiceCommand(text);
        if (string.IsNullOrWhiteSpace(clean)) return null;

        string lower = clean.ToLowerInvariant().Trim();

        // 1. Direct Web Targets (YouTube / GitHub)
        if (lower is "open youtube" or "youtube")
        {
            return new VoiceIntent(VoiceActionType.OpenUrl, "https://youtube.com", "Opened YouTube.", "Opened YouTube.");
        }
        if (lower is "open github" or "github")
        {
            return new VoiceIntent(VoiceActionType.OpenUrl, "https://github.com", "Opened GitHub.", "Opened GitHub.");
        }
        if (lower is "open spotify" or "spotify" or "open spotifi" or "spotifi" or "opensoptifi" or "openspotify")
        {
            return new VoiceIntent(VoiceActionType.LaunchApp, "spotify:", "Launched Spotify.", "Opening Spotify.");
        }

        // Media Controls
        if (lower is "play" or "play music" or "resume")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "play", "Playing music.", "Playing music.");
        }
        if (lower is "pause" or "pause music" or "stop music")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "pause", "Paused playback.", "Paused music.");
        }
        if (lower is "next" or "next song" or "next track" or "skip" or "skip song")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "next", "Skipped track.", "Next track.");
        }
        if (lower is "previous" or "previous song" or "previous track")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "previous", "Previous track.", "Previous track.");
        }
        if (lower is "mute")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "mute", "Muted volume.", "Muted.");
        }
        if (lower is "unmute")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "unmute", "Unmuted volume.", "Unmuted.");
        }
        if (lower is "volume up" or "louder")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "volup", "Volume increased.", "Volume up.");
        }
        if (lower is "volume down" or "quieter")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "voldown", "Volume decreased.", "Volume down.");
        }
        var volMatch = Regex.Match(lower, @"^(?:set\s+)?volume\s+(?:to\s+)?(\d{1,3})%?$");
        if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out int vVal))
        {
            int clamped = Math.Clamp(vVal, 0, 100);
            return new VoiceIntent(VoiceActionType.MediaControl, $"volume_{clamped}", $"Volume set to {clamped}%.", $"Volume {clamped} percent.");
        }
        if (lower is "volume zero" or "volume 0" or "volume 0%")
        {
            return new VoiceIntent(VoiceActionType.MediaControl, "volume_0", "Volume set to 0%.", "Volume muted.");
        }

        // 2. App Launch
        var openMatch = Regex.Match(lower, @"^(?:can you\s+)?(?:open|launch|start)\s+([a-z0-9_\-\.\s]+)$");
        if (openMatch.Success)
        {
            string appName = openMatch.Groups[1].Value.Trim();
            if (TryResolveAppTarget(appName, out string target, out string title))
            {
                return new VoiceIntent(VoiceActionType.LaunchApp, target, $"Opened {title}.", $"Opened {title}.");
            }
        }

        // 2b. App Close
        var closeMatch = Regex.Match(lower, @"^(?:can you\s+)?close\s+([a-z0-9_\-\.\s]+)$");
        if (closeMatch.Success)
        {
            string appName = closeMatch.Groups[1].Value.Trim();
            if (TryResolveAppTarget(appName, out string target, out string title))
            {
                return new VoiceIntent(VoiceActionType.CloseApp, target, $"Closed {title}.", $"Closed {title}.");
            }
        }

        // 3. Web Search
        var searchMatch = Regex.Match(lower, @"^(?:search for|google|search)\s+(.+)$");
        if (searchMatch.Success)
        {
            string query = searchMatch.Groups[1].Value.Trim();
            string url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
            return new VoiceIntent(VoiceActionType.SearchWeb, url, $"Searching Google for \"{query}\"", $"Searching for {query}.");
        }

        // 4. System Time
        if (lower.Contains("what time") || lower == "time")
        {
            string t = DateTime.Now.ToString("t");
            return new VoiceIntent(VoiceActionType.SystemTime, t, $"Current time is {t}", $"It is {t}.");
        }

        // 5. System Date
        if (lower.Contains("what date") || lower.Contains("what day") || lower == "date" || lower == "today")
        {
            string d = DateTime.Now.ToString("dddd, MMMM d");
            return new VoiceIntent(VoiceActionType.SystemDate, d, $"Today is {d}", $"Today is {d}.");
        }

        // 6. Greetings
        if (lower is "hello" or "hi" or "hey" or "good morning" or "good evening")
        {
            return new VoiceIntent(VoiceActionType.Greeting, lower, "Hello! How can I help you?", "Hello! What can I do for you?");
        }

        // 7. Jokes
        if (lower.Contains("tell me a joke") || lower == "joke")
        {
            string[] jokes =
            [
                "Why do programmers prefer dark mode? Because light attracts bugs.",
                "There are 10 types of people in the world: those who understand binary, and those who don't.",
                "A SQL query walks into a bar, walks up to two tables and asks, 'Can I join you?'"
            ];
            string j = jokes[Random.Shared.Next(jokes.Length)];
            return new VoiceIntent(VoiceActionType.Joke, j, j, j);
        }

        // 8. Screenshot
        if (lower is "take a screenshot" or "take screenshot" or "screenshot" or "screen shot" or "capture screen" or "get screenshot")
        {
            return new VoiceIntent(VoiceActionType.Screenshot, "screenshot", "Captured screenshot.", "Screenshot captured.");
        }

        // 9. Brightness
        if (lower is "brightness up" or "increase brightness" or "make it brighter")
        {
            return new VoiceIntent(VoiceActionType.Brightness, "+15", "Brightness increased.", "Brightness up.");
        }
        if (lower is "brightness down" or "decrease brightness" or "dim screen" or "lower brightness")
        {
            return new VoiceIntent(VoiceActionType.Brightness, "-15", "Brightness decreased.", "Brightness down.");
        }
        var brightMatch = Regex.Match(lower, @"^(?:set\s+)?brightness\s+(?:to\s+)?(\d{1,3})%?$");
        if (brightMatch.Success && int.TryParse(brightMatch.Groups[1].Value, out int bVal))
        {
            int clamped = Math.Clamp(bVal, 0, 100);
            return new VoiceIntent(VoiceActionType.Brightness, clamped.ToString(), $"Brightness set to {clamped}%.", $"Brightness {clamped} percent.");
        }

        return null;
    }
}

/// <summary>
/// Zero-allocation, high-frequency motion analyzer that detects intentional back-and-forth window shaking
/// while filtering out steady dragging, circular paths, and jitter.
/// </summary>
public enum VoiceActionType
{
    None,
    LaunchApp,
    SearchWeb,
    OpenUrl,
    SystemTime,
    SystemDate,
    Greeting,
    Joke,
    MediaControl,
    Screenshot,
    Brightness,
    CloseApp
}

public sealed record VoiceIntent(VoiceActionType Type, string Target, string DisplaySummary, string ReplyVoice);

public struct WindowShakeDetector
{
    private int _lastX;
    private int _lastY;
    private int _lastDirX;
    private int _lastDirY;
    private long _lastReversalTime;
    private int _reversalCount;
    private bool _initialized;

    public const int MinDisplacement = 28;      // Minimum pixels per stroke to count as an intentional gesture
    public const int MaxStrokeIntervalMs = 500; // Max milliseconds allowed between stroke reversals
    public const int RequiredReversals = 3;     // Reversals needed to trigger unpin (e.g. Left -> Right -> Left -> Right = 3)

    public void Reset()
    {
        _lastX = 0;
        _lastY = 0;
        _lastDirX = 0;
        _lastDirY = 0;
        _lastReversalTime = 0;
        _reversalCount = 0;
        _initialized = false;
    }

    /// <summary>
    /// Evaluates cursor position sample during window drag. Returns true when a vigorous shake gesture is detected.
    /// </summary>
    public bool RegisterPoint(int x, int y, long currentTimeMs)
    {
        if (!_initialized)
        {
            _lastX = x;
            _lastY = y;
            _lastReversalTime = currentTimeMs;
            _initialized = true;
            return false;
        }

        int dx = x - _lastX;
        int dy = y - _lastY;
        int distSq = dx * dx + dy * dy;

        // Ignore micro tremors below threshold
        if (distSq < MinDisplacement * MinDisplacement)
        {
            return false;
        }

        int currentDirX = Math.Abs(dx) >= MinDisplacement ? (dx > 0 ? 1 : -1) : 0;
        int currentDirY = Math.Abs(dy) >= MinDisplacement ? (dy > 0 ? 1 : -1) : 0;

        bool reversed = false;
        if (currentDirX != 0 && _lastDirX != 0 && currentDirX != _lastDirX)
        {
            reversed = true;
        }
        else if (currentDirY != 0 && _lastDirY != 0 && currentDirY != _lastDirY)
        {
            reversed = true;
        }

        if (reversed)
        {
            long elapsed = currentTimeMs - _lastReversalTime;
            if (elapsed <= MaxStrokeIntervalMs)
            {
                _reversalCount++;
                _lastReversalTime = currentTimeMs;

                if (_reversalCount >= RequiredReversals)
                {
                    Reset();
                    return true;
                }
            }
            else
            {
                // Stroke took too long, restart sequence
                _reversalCount = 1;
                _lastReversalTime = currentTimeMs;
            }
        }
        else if (_lastDirX == 0 && _lastDirY == 0)
        {
            _lastReversalTime = currentTimeMs;
        }

        if (currentDirX != 0) _lastDirX = currentDirX;
        if (currentDirY != 0) _lastDirY = currentDirY;
        _lastX = x;
        _lastY = y;

        return false;
    }
}

