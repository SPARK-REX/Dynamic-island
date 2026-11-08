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
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int SW_RESTORE = 9;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_EXITSIZEMOVE      = 0x0232;   // fired when any move/resize drag ends

    public const uint GW_HWNDFIRST = 0;
    public const uint GW_HWNDLAST = 1;
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public static readonly IntPtr HWND_TOP = new IntPtr(0);
    public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    public const uint GA_ROOTOWNER = 3;

    public const uint EVENT_SYSTEM_FOREGROUND    = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;   // fired when a window starts being dragged
    public const uint EVENT_SYSTEM_MOVESIZEEND   = 0x000B;   // fired when a window drag is released
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>
    /// Flushes unused working set pages to the operating system standby list,
    /// dropping the physical memory footprint shown in Task Manager down to ~20-35MB.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            IntPtr hProcess = GetCurrentProcess();
            EmptyWorkingSet(hProcess);
            SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

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
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // High-performance process name cache to eliminate CPU spikes and object churn from Process.GetProcessById
    private static readonly ConcurrentDictionary<uint, (string Name, long Expiry)> ProcessNameCache = new();

    /// <summary>
    /// Retrieves the lowercase process name for a process ID, caching results for 15 seconds
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
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                {
                    string fullPath = sb.ToString();
                    name = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
                }
            }
            catch { }
            finally
            {
                CloseHandle(hProc);
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName.ToLowerInvariant();
            }
            catch { }
        }

        if (ProcessNameCache.Count > 128)
        {
            foreach (var kvp in ProcessNameCache)
            {
                if (kvp.Value.Expiry <= now)
                    ProcessNameCache.TryRemove(kvp.Key, out _);
            }
        }

        ProcessNameCache[pid] = (name, now + 15000);
        return name;
    }

    /// <summary>
    /// Detects if the foreground window is currently running in full-screen mode on the monitor where islandHwnd is placed.
    /// </summary>
    public static bool IsForegroundWindowFullScreenOnIslandMonitor(IntPtr islandHwnd)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == islandHwnd) return false;

            IntPtr desktop = GetDesktopWindow();
            IntPtr shell = GetShellWindow();
            if (fg == desktop || fg == shell) return false;

            var className = new StringBuilder(64);
            GetClassName(fg, className, 64);
            string cls = className.ToString();
            if (cls is "Progman" or "WorkerW") return false;

            if (!GetWindowRect(fg, out RECT fgRect) || fgRect.Width <= 0 || fgRect.Height <= 0)
                return false;

            IntPtr islandMon = MonitorFromWindow(islandHwnd, MONITOR_DEFAULTTOPRIMARY);
            if (islandMon == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(islandMon, ref mi)) return false;

            return IslandLogic.IsRectFullScreen(
                fgRect.Left, fgRect.Top, fgRect.Right, fgRect.Bottom,
                mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if any visible, non-minimized window is currently placed in front of our window in the Z-order,
    /// or if the window has lost its WS_EX_TOPMOST extended style.
    /// </summary>
    public static bool NeedsTopmostEnforcement(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
            return false;

        try
        {
            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOPMOST) == 0)
                return true;

            IntPtr prev = GetWindow(hWnd, GW_HWNDPREV);
            if (prev == IntPtr.Zero)
                return false;

            while (prev != IntPtr.Zero)
            {
                if (IsWindowVisible(prev) && !IsIconic(prev))
                {
                    if (GetWindowRect(prev, out RECT r) && r.Width > 0 && r.Height > 0)
                    {
                        return true;
                    }
                }
                prev = GetWindow(prev, GW_HWNDPREV);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Forces the specified window to the front of all topmost windows without stealing focus (SWP_NOACTIVATE).
    /// If another topmost window (such as a fullscreen app) is still in front, cycles HWND_NOTOPMOST -> HWND_TOPMOST
    /// to re-insert our window at the head of the system Z-order.
    /// </summary>
    public static void ForceTopmost(IntPtr hWnd, Action? beforeReset = null, Action? afterReset = null)
    {
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
            return;

        try
        {
            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOPMOST) == 0)
            {
                SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOPMOST));
            }

            SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

            if (NeedsTopmostEnforcement(hWnd))
            {
                beforeReset?.Invoke();

                SetWindowPos(
                    hWnd,
                    HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

                SetWindowPos(
                    hWnd,
                    HWND_TOPMOST,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

                afterReset?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
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

    /// <summary>
    /// Checks whether an external window currently has the WS_EX_TOPMOST style.
    /// </summary>
    public static bool IsWindowTopmost(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
        long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        return (exStyle & WS_EX_TOPMOST) != 0;
    }

    /// <summary>
    /// Pins or unpins an external window (e.g. Chrome, browser tab, or app) to stay on top of all applications.
    /// </summary>
    public static bool SetWindowTopmost(IntPtr hWnd, bool topmost)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
        try
        {
            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            if (topmost)
                exStyle |= WS_EX_TOPMOST;
            else
                exStyle &= ~WS_EX_TOPMOST;
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));

            IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            return SetWindowPos(
                hWnd,
                insertAfter,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }
    }

    /// <summary>
    /// Safely gets the window text/title of an external window.
    /// </summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return "";
        try
        {
            var sb = new StringBuilder(512);
            int len = GetWindowText(hWnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Validates whether a window handle belongs to a normal application window that can be pinned on top,
    /// excluding the island itself, invisible windows, desktop icons, and taskbar elements.
    /// </summary>
    public static bool IsValidPinnableWindow(IntPtr hWnd, IntPtr islandHwnd)
    {
        if (hWnd == IntPtr.Zero || hWnd == islandHwnd || !IsWindow(hWnd)) return false;
        if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return false;

        try
        {
            var sb = new StringBuilder(128);
            GetClassName(hWnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return IslandLogic.IsValidPinnableClassName(cls);
        }
        catch
        {
            return false;
        }
    }
}
