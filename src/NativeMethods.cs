// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland;

/// <summary>
/// Centralized Win32 interop definitions with 64-bit pointer safety and high-efficiency caching.
/// </summary>
internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_RESTORE = 9;
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>64-bit and 32-bit safe GetWindowLongPtr wrapper.</summary>
    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    /// <summary>64-bit and 32-bit safe SetWindowLongPtr wrapper.</summary>
    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong.ToInt64())));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // High-performance process name cache to eliminate CPU spikes and object churn from Process.GetProcessById
    private static readonly ConcurrentDictionary<uint, (string Name, long Expiry)> ProcessNameCache = new();

    /// <summary>
    /// Retrieves the lowercase process name for a process ID, caching results for 8 seconds
    /// to avoid continuous kernel handle opens and GC allocations.
    /// </summary>
    public static string GetProcessNameCached(uint pid)
    {
        if (pid == 0) return "";
        long now = Environment.TickCount64;

        if (ProcessNameCache.TryGetValue(pid, out var cached) && cached.Expiry > now)
        {
            return cached.Name;
        }

        string name = "";
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName.ToLowerInvariant();
        }
        catch
        {
            // Process terminated or access denied
        }

        if (ProcessNameCache.Count > 128)
        {
            foreach (var kvp in ProcessNameCache)
            {
                if (kvp.Value.Expiry <= now)
                    ProcessNameCache.TryRemove(kvp.Key, out _);
            }
        }

        ProcessNameCache[pid] = (name, now + 8000);
        return name;
    }

    /// <summary>
    /// Safely starts a packaged UWP app by AppUserModelId via explorer.exe shell:AppsFolder.
    /// Uses absolute system directory path to prevent untrusted binary hijack.
    /// </summary>
    public static bool StartPackagedApp(string appUserModelId)
    {
        try
        {
            string explorerPath = Path.Combine(Environment.SystemDirectory, "explorer.exe");
            var psi = new ProcessStartInfo(explorerPath, $"shell:AppsFolder\\{appUserModelId}")
            {
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }
    }
}
