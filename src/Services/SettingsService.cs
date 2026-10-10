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

    public bool Use24HourClock { get; private set; }
    public bool AlwaysShowChargingIndicator { get; private set; }
    public bool UseBackdropBlur { get; private set; } = true;

    public event Action<bool>? ClockFormatChanged;

    public SettingsService()
    {
        LoadClockFormat();
        LoadChargingIndicator();
        try { if (File.Exists(BlurSettingsFile)) UseBackdropBlur = File.ReadAllText(BlurSettingsFile).Trim() != "0"; }
        catch { UseBackdropBlur = true; }
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
