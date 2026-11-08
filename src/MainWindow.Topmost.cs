// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// Active topmost enforcement and window pin-on-drop engine.
/// 
/// 1. Topmost enforcement: Keeps Dynamic Island pinned at the absolute front of the Z-order above all
///    applications, even when games, media players, or browsers enter full-screen mode.
/// 
/// 2. External window drag & drop (Pin-to-Top): When you start dragging an external application window,
///    Dynamic Island smoothly awakens and displays "Drag here to pin" with the app name.
///    Hovering over the island illuminates the target into "Drop to pin on top".
///    Dropping the window pins it on top of all applications, or unpins it if already topmost.
/// </summary>
public partial class MainWindow : Window
{
    private DispatcherTimer? _topmostTimer;

    // Foreground-change hook (keeps island above fullscreen apps)
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private IntPtr _winEventHook = IntPtr.Zero;

    // Move/size hook (detects external app drags dropped on the island)
    private NativeMethods.WinEventDelegate? _moveSizeDelegate;
    private IntPtr _moveSizeHook = IntPtr.Zero;
    private IntPtr _draggingExternalHwnd = IntPtr.Zero;

    // Drag-to-pin tracking state
    private bool _isDraggingExternalApp;
    private bool _isHoveringPinZone;
    private Mode _preDragMode = Mode.Idle;
    private IntPtr _activeDraggingHwnd = IntPtr.Zero;
    private DispatcherTimer? _dragTrackingTimer;
    private DispatcherTimer? _dragSafetyTimer;
    private WindowShakeDetector _shakeDetector;

    private bool _isResettingTopmost;
    private IntPtr _hwnd = IntPtr.Zero;

    /// <summary>
    /// Initializes active topmost enforcement and OS-wide window drag detection.
    /// </summary>
    private void InitTopmostEnforcer()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return;

        // 1. Initial topmost assertion
        EnforceTopmost();

        // 2. Hook foreground window changes (fullscreen app detection)
        try
        {
            _winEventDelegate = OnForegroundChangedWinEvent;
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to set foreground WinEventHook: {ex.Message}");
        }

        // 3. Hook move/size start+end across all processes (drop-on-island detection)
        try
        {
            _moveSizeDelegate = OnMoveSizeEvent;
            _moveSizeHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
                NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero,
                _moveSizeDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to set move/size WinEventHook: {ex.Message}");
        }

        // 4. Heartbeat timer (adaptive: 800ms when fullscreen app active, 2500ms otherwise)
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _topmostTimer.Tick += (_, _) => CheckAndEnforceTopmost();
        _topmostTimer.Start();
    }

    private void ShutdownTopmostEnforcer()
    {
        _topmostTimer?.Stop();
        _topmostTimer = null;

        _dragTrackingTimer?.Stop();
        _dragTrackingTimer = null;
        _dragSafetyTimer?.Stop();
        _dragSafetyTimer = null;

        foreach (var hook in new[] { _winEventHook, _moveSizeHook })
        {
            if (hook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWinEvent(hook); }
                catch (Exception ex) { Program.Log(ex); }
            }
        }
        _winEventHook = IntPtr.Zero;
        _moveSizeHook = IntPtr.Zero;
        _winEventDelegate = null;
        _moveSizeDelegate = null;
    }

    private void OnForegroundChangedWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_settingsService.AlwaysOnTopFullscreen) return;
        Dispatcher.InvokeAsync(EnforceTopmost, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Handles EVENT_SYSTEM_MOVESIZESTART and EVENT_SYSTEM_MOVESIZEEND across the system.
    /// </summary>
    private void OnMoveSizeEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only respond to window-level events
        if (idObject != 0) return;

        if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
        {
            _draggingExternalHwnd = hwnd;
            Dispatcher.InvokeAsync(() => OnExternalWindowDragStarted(hwnd), DispatcherPriority.Normal);
        }
        else if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND)
        {
            IntPtr droppedHwnd = hwnd != IntPtr.Zero ? hwnd : _draggingExternalHwnd;
            _draggingExternalHwnd = IntPtr.Zero;

            if (droppedHwnd == IntPtr.Zero) return;

            Dispatcher.InvokeAsync(() => OnExternalWindowDragEnded(droppedHwnd), DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Activated when the user begins dragging an external application window.
    /// Morphs Dynamic Island into the "Drag here to pin" capsule with the application identity.
    /// </summary>
    private void OnExternalWindowDragStarted(IntPtr hwnd)
    {
        if (Visibility != Visibility.Visible) return;
        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return;

        // Resolve the root top-level window (e.g. Chrome main window if caption/tab dragged)
        IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

        if (rootHwnd == _hwnd || !NativeMethods.IsValidPinnableWindow(rootHwnd, _hwnd))
            return;

        _isDraggingExternalApp = true;
        _activeDraggingHwnd = rootHwnd;
        _preDragMode = _mode == Mode.PinTarget ? _preDragMode : _mode;

        string title = NativeMethods.GetWindowTitle(rootHwnd);
        NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint pid);
        string proc = NativeMethods.GetProcessNameCached(pid);
        string appName = IslandLogic.FormatPinnedAppName(title, proc);
        bool isTopmost = NativeMethods.IsWindowTopmost(rootHwnd);

        PinTargetAppName.Text = appName;
        UpdatePinPromptState(isHovering: false, isTopmost);

        Go(Mode.PinTarget);

        StartDragTracking(rootHwnd, isTopmost);
    }

    private void StartDragTracking(IntPtr rootHwnd, bool isTopmost)
    {
        _dragTrackingTimer?.Stop();
        _dragSafetyTimer?.Stop();

        _isHoveringPinZone = false;
        _shakeDetector.Reset();

        _dragTrackingTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _dragTrackingTimer.Tick += (_, _) =>
        {
            if (!_isDraggingExternalApp || Visibility != Visibility.Visible)
            {
                _dragTrackingTimer?.Stop();
                return;
            }

            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt) ||
                !NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT islandRect))
            {
                return;
            }

            // Shake-to-Unpin: only active when dragging an already-pinned window
            if (isTopmost && _shakeDetector.RegisterPoint(pt.X, pt.Y, Environment.TickCount64))
            {
                ExecuteShakeToUnpin(rootHwnd);
                return;
            }

            bool hovering = IslandLogic.IsDropOnIsland(
                pt.X, pt.Y,
                islandRect.Left, islandRect.Top, islandRect.Right, islandRect.Bottom,
                padding: 16.0);

            if (hovering != _isHoveringPinZone)
            {
                _isHoveringPinZone = hovering;
                UpdatePinPromptState(hovering, isTopmost);
            }
        };
        _dragTrackingTimer.Start();

        // Safety fallback timer: auto-revert if dragging window was killed/closed without MOVESIZEEND
        _dragSafetyTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _dragSafetyTimer.Tick += (_, _) =>
        {
            _dragSafetyTimer?.Stop();
            if (_isDraggingExternalApp)
            {
                CancelDragState();
            }
        };
        _dragSafetyTimer.Start();
    }

    private void UpdatePinPromptState(bool isHovering, bool isTopmost)
    {
        PinPromptTitle.Text = IslandLogic.FormatPinPromptTitle(isHovering, isTopmost);

        var (r, g, b) = IslandLogic.GetPinBadgeColor(isHovering, isTopmost);
        PinBadgeGlow.Color = Color.FromRgb(r, g, b);
        PinBadge.Background = new LinearGradientBrush(
            Color.FromRgb(r, g, b),
            Color.FromRgb((byte)Math.Max(0, r - 30), (byte)Math.Max(0, g - 30), (byte)Math.Max(0, b - 30)),
            new Point(0, 0), new Point(1, 1));

        if (isHovering)
        {
            PinTargetPill.Background = new SolidColorBrush(Color.FromArgb(0x40, r, g, b));
            PinTargetPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, r, g, b));
            PinBadgeGlyph.Text = "\uE840";
        }
        else
        {
            PinTargetPill.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            PinTargetPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
            PinBadgeGlyph.Text = "\uE840";
        }
    }

    /// <summary>
    /// Called when an external window drag ends. If dropped on Dynamic Island,
    /// toggles topmost pinning and notifies the user. If released elsewhere, smoothly restores prior state.
    /// </summary>
    private void OnExternalWindowDragEnded(IntPtr droppedHwnd)
    {
        _dragTrackingTimer?.Stop();
        _dragTrackingTimer = null;
        _dragSafetyTimer?.Stop();
        _dragSafetyTimer = null;

        if (!_isDraggingExternalApp) return;
        _isDraggingExternalApp = false;
        _isHoveringPinZone = false;

        if (Visibility != Visibility.Visible) return;
        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return;

        // Check if cursor was released on the island
        if (!NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT islandRect) ||
            !NativeMethods.GetCursorPos(out NativeMethods.POINT cursor) ||
            !IslandLogic.IsDropOnIsland(cursor.X, cursor.Y, islandRect.Left, islandRect.Top, islandRect.Right, islandRect.Bottom, padding: 16.0))
        {
            // Released elsewhere: restore previous island mode
            Go(_preDragMode != Mode.PinTarget ? _preDragMode : Rest());
            return;
        }

        // Dropped on the island!
        IntPtr targetHwnd = _activeDraggingHwnd != IntPtr.Zero ? _activeDraggingHwnd : droppedHwnd;
        _activeDraggingHwnd = IntPtr.Zero;

        IntPtr rootHwnd = NativeMethods.GetAncestor(targetHwnd, NativeMethods.GA_ROOTOWNER);
        if (rootHwnd == IntPtr.Zero) rootHwnd = targetHwnd;

        if (rootHwnd == _hwnd || !NativeMethods.IsValidPinnableWindow(rootHwnd, _hwnd))
        {
            Go(_preDragMode != Mode.PinTarget ? _preDragMode : Rest());
            return;
        }

        // Toggle topmost
                bool wasTopmost = NativeMethods.IsWindowTopmost(rootHwnd);
        string title = NativeMethods.GetWindowTitle(rootHwnd);
        NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint pid);
        string proc = NativeMethods.GetProcessNameCached(pid);
        string appName = IslandLogic.FormatPinnedAppName(title, proc);

        // REQUIREMENT: Only unpin via shake gesture. Dragging or dropping an already-pinned window on the island must NOT unpin it!
        if (wasTopmost)
        {
            Program.Log($"[DynamicIsland] Window already pinned, ignoring drop unpin: hwnd=0x{rootHwnd:X}, app={appName}");
            Notify("\uE840 Already Pinned", $"{appName} is pinned on top. Shake window to unpin.", proc, 3.0);
            Go(_preDragMode != Mode.PinTarget ? _preDragMode : Rest());
            return;
        }

        // Window is not topmost: pin it on top
        NativeMethods.SetWindowTopmost(rootHwnd, true);
        EnforceTopmost();

        Program.Log($"[DynamicIsland] Pinned window on top of all apps: hwnd=0x{rootHwnd:X}, app={appName}, proc={proc}");
        Notify("\uE840 Pinned on Top", $"{appName} will stay on top of all apps", proc, 4.0);
    }

    private void CancelDragState()
    {
        _dragTrackingTimer?.Stop();
        _dragTrackingTimer = null;
        _dragSafetyTimer?.Stop();
        _dragSafetyTimer = null;
        _isDraggingExternalApp = false;
        _isHoveringPinZone = false;
        _activeDraggingHwnd = IntPtr.Zero;

        if (_mode == Mode.PinTarget)
        {
            Go(_preDragMode != Mode.PinTarget ? _preDragMode : Rest());
        }
    }

    /// <summary>
    /// Instantly unpins an already-pinned window when a vigorous shake gesture is detected during drag.
    /// Cancels active drag tracking and presents visual confirmation.
    /// </summary>
    private void ExecuteShakeToUnpin(IntPtr rootHwnd)
    {
        _dragTrackingTimer?.Stop();
        _dragTrackingTimer = null;
        _dragSafetyTimer?.Stop();
        _dragSafetyTimer = null;

        _isDraggingExternalApp = false;
        _isHoveringPinZone = false;
        _activeDraggingHwnd = IntPtr.Zero;
        _shakeDetector.Reset();

        // 1. Unpin the window
        NativeMethods.SetWindowTopmost(rootHwnd, false);
        EnforceTopmost();

        // 2. Resolve process and application name
        string title = NativeMethods.GetWindowTitle(rootHwnd);
        NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint pid);
        string proc = NativeMethods.GetProcessNameCached(pid);
        string appName = IslandLogic.FormatPinnedAppName(title, proc);

        Program.Log($"[DynamicIsland] Shake to Unpin triggered for hwnd=0x{rootHwnd:X}, app={appName}, proc={proc}");

        // 3. Visual notification feedback
        Notify("📌 Unpinned", $"{appName} unpinned (Shake to Unpin)", proc, 3.5);
    }

    private void CheckAndEnforceTopmost()
    {
        if (!_settingsService.AlwaysOnTopFullscreen) return;
        if (Visibility != Visibility.Visible) return;

        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return;

        bool isFullScreen = NativeMethods.IsForegroundWindowFullScreenOnIslandMonitor(_hwnd);

        TimeSpan targetInterval = isFullScreen ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromMilliseconds(2500);
        if (_topmostTimer != null && _topmostTimer.Interval != targetInterval)
        {
            _topmostTimer.Interval = targetInterval;
        }

        if (NativeMethods.NeedsTopmostEnforcement(_hwnd) || (isFullScreen && NativeMethods.GetWindow(_hwnd, NativeMethods.GW_HWNDPREV) != IntPtr.Zero))
        {
            EnforceTopmost();
        }
    }

    /// <summary>
    /// Forces the island to the top of the Z-order without stealing user focus.
    /// </summary>
    public void EnforceTopmost()
    {
        if (!_settingsService.AlwaysOnTopFullscreen) return;
        if (Visibility != Visibility.Visible) return;

        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return;

        NativeMethods.ForceTopmost(
            _hwnd,
            beforeReset: () => _isResettingTopmost = true,
            afterReset:  () => _isResettingTopmost = false);
    }

    /// <summary>
    /// Handles WM_WINDOWPOSCHANGING to prevent the system or other windows from dropping our Z-order.
    /// </summary>
    private void HandleWindowPosChanging(IntPtr lParam)
    {
        if (_isResettingTopmost || !_settingsService.AlwaysOnTopFullscreen) return;

        try
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if (pos.hwndInsertAfter != NativeMethods.HWND_TOPMOST)
            {
                pos.hwndInsertAfter = NativeMethods.HWND_TOPMOST;
                pos.flags &= ~NativeMethods.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }
}
