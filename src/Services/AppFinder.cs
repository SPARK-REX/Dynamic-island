// ====================================================================================================
// Finds and launches ANY installed Windows app by spoken name (Win32, Store/UWP, Start Menu shortcuts).
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland.Services;

public static class AppFinder
{
    internal readonly record struct AppEntry(string Name, string Key, string Target);

    private static readonly object _gate = new();
    private static List<AppEntry>? _index;
    private static DateTime _builtUtc = DateTime.MinValue;

    /// <summary>Build the app index in background so the first voice command is instant.</summary>
    public static void Warmup() => _ = Task.Run(() => Index(false));

    /// <summary>Launch app by spoken name. False = not found / failed to start.</summary>
    public static bool TryLaunch(string spoken)
    {
        string key = Norm(StripNoise(spoken));
        if (key.Length == 0) return false;

        // 1. Exact / prefix match in installed apps (also proves the app exists).
        if (LaunchHit(Pick(Index(false), key, strict: true))) return true;

        // 2. Built-in alias table (vs code, cmd, settings...).
        if (IslandLogic.TryResolveAppTarget(spoken, out string alias, out _) &&
            !alias.Equals(spoken.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (!alias.EndsWith(':') || ProtocolRegistered(alias)) &&
            Start(alias)) return true;

        // 3. Loose match (contains / all words).
        if (LaunchHit(Pick(Index(false), key, strict: false))) return true;

        // 4. Maybe app was installed after startup: rescan once (max every 20s).
        if (LaunchHit(Pick(Index(true), key, strict: false))) return true;

        // 5. Last resort: plain exe name on PATH / App Paths ("mspaint", "winword"). Names only, no paths/urls.
        string plain = spoken.Trim();
        return Regex.IsMatch(plain, @"^[\w\.\- ]{1,60}$") && Start(plain);
    }

    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    { "explorer", "dwm", "winlogon", "csrss", "system", "services", "lsass", "svchost", "dynamicisland", "applicationframehost" };

    /// <summary>Close every window/process matching the spoken name. Returns how many were closed.</summary>
    public static int Close(string spoken)
    {
        string key = Norm(StripNoise(spoken));
        if (key.Length < 2) return 0;
        int closed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (Protected.Contains(p.ProcessName) || p.Id == Environment.ProcessId) continue;
                bool hasWindow = p.MainWindowHandle != IntPtr.Zero;
                string pn = Norm(p.ProcessName);
                // Name match (any process) or window-title match (visible windows only, covers Store apps).
                bool match = pn == key || pn.StartsWith(key, StringComparison.Ordinal) ||
                             (hasWindow && Norm(p.MainWindowTitle).Contains(key, StringComparison.Ordinal));
                if (!match) continue;

                // Polite close first (lets app ask to save); kill only windowless/stuck processes.
                if (hasWindow && p.CloseMainWindow() && p.WaitForExit(2500)) { closed++; continue; }
                if (!hasWindow) { p.Kill(); closed++; }
            }
            catch (Exception ex) { Program.Log($"AppFinder close '{spoken}': {ex.Message}"); }
            finally { p.Dispose(); }
        }
        return closed;
    }

    private static bool LaunchHit(AppEntry? hit) => hit is { } e && Start(e.Target);

    // ---- Matching (pure, unit-tested) ----

    internal static string Norm(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    internal static string StripNoise(string s) =>
        Regex.Replace(s.Trim(), @"^(?:the\s+)|(?:\s+(?:app|application|program))$", "", RegexOptions.IgnoreCase);

    internal static AppEntry? Pick(IReadOnlyList<AppEntry> entries, string key, bool strict)
    {
        if (key.Length == 0) return null;
        IEnumerable<AppEntry> pool = entries.Where(e => !e.Key.Contains("uninstall"));

        AppEntry? best(Func<AppEntry, bool> f) =>
            pool.Where(f).OrderBy(e => e.Key.Length).Cast<AppEntry?>().FirstOrDefault();

        var hit = best(e => e.Key == key) ?? best(e => e.Key.StartsWith(key, StringComparison.Ordinal));
        if (hit != null || strict || key.Length < 3) return hit;
        return best(e => e.Key.Contains(key, StringComparison.Ordinal));
    }

    // ---- Index ----

    private static List<AppEntry> Index(bool forceRescan)
    {
        lock (_gate)
        {
            if (_index != null && (!forceRescan || DateTime.UtcNow - _builtUtc < TimeSpan.FromSeconds(20)))
                return _index;
            _index = Build();
            _builtUtc = DateTime.UtcNow;
            return _index;
        }
    }

    private static List<AppEntry> Build()
    {
        var map = new Dictionary<string, AppEntry>();

        void add(string name, string target)
        {
            string key = Norm(name);
            if (key.Length > 0) map.TryAdd(key, new AppEntry(name, key, target));
        }

        // Start Menu shortcuts (fast, always works)
        foreach (var root in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            try
            {
                string dir = Environment.GetFolderPath(root);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                    add(Path.GetFileNameWithoutExtension(f), f);
            }
            catch (Exception ex) { Program.Log($"AppFinder start-menu scan: {ex.Message}"); }
        }

        // Store/UWP + everything else Windows lists as an app
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            });
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    int bar = line.LastIndexOf('|');
                    if (bar <= 0) continue;
                    string id = line[(bar + 1)..].Trim();
                    if (id.Length > 0) add(line[..bar].Trim(), "shell:AppsFolder\\" + id);
                }
            }
        }
        catch (Exception ex) { Program.Log($"AppFinder Get-StartApps: {ex.Message}"); }

        Program.Log($"AppFinder indexed {map.Count} apps.");
        return map.Values.ToList();
    }

    // ---- Launch ----

    private static bool ProtocolRegistered(string target)
    {
        try { return Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(target.TrimEnd(':')) != null; }
        catch { return false; }
    }

    private static bool Start(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Program.Log($"AppFinder launch '{target}' failed: {ex.Message}");
            return false;
        }
    }
}
