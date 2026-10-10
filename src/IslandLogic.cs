// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
}

