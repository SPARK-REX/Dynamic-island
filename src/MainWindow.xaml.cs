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
    private enum Mode { Idle, Compact, Media, Volume, Notify, Timer, Charging }

    // ---- services ------------------------------------------------------------------------
    private readonly SettingsService _settingsService = new();
    private readonly AudioVolumeService _audioService = new();
    private readonly FocusTimerService _timerService = new();
    private readonly MediaService _mediaService = new();
    private readonly NotificationService _notificationService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly PowerService _powerService = new();

    // ---- state ---------------------------------------------------------------------------
    private Mode _mode = Mode.Idle;
    private bool _hover;                  // mouse is over the island
    private bool _playing;                // session is actively playing -> compact pill
    private bool _barsOn;                 // equalizer animation currently running
    private bool _isCharging;             // AC charger is actively plugged in

    // ---- timers --------------------------------------------------------------------------
    private readonly DispatcherTimer _trim = new() { Interval = TimeSpan.FromSeconds(8) };   // debounce memory trim
    private readonly DispatcherTimer _clock = new();    // ticks once per minute, aligned to :00
    private readonly DispatcherTimer _revert = new();   // "go back to idle in N s"
    private readonly DispatcherTimer _privacyTimer = new() { Interval = TimeSpan.FromSeconds(1.0) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(15) };

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
    private void SetBackdrop(bool blur, bool save = true)
    {
        if (blur) blur = _backdrop?.Start() == true;
        else _backdrop?.Stop();
        _itemBlur.Checked = blur;
        _itemBlack.Checked = !blur;
        if (save) _settingsService.SetBackdropBlur(blur);
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
        foreach (var panel in new UIElement[] { IdlePanel, CompactPanel, MediaPanel, VolumePanel, NotifyPanel, TimerPanel, ChargingPanel })
        {
            panel.RenderTransformOrigin = new Point(0.5, 0.5);
            panel.RenderTransform = new ScaleTransform(0.9, 0.9);
        }
        IdlePanel.Opacity = 1;
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
            Go(_hover ? Mode.Media : Rest());
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

        var hide = new System.Windows.Forms.ToolStripMenuItem("Hide island");
        void ToggleIsland()
        {
            Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
            hide.Text = Visibility == Visibility.Visible ? "Hide island" : "Show island";
        }
        hide.Click += (_, _) => ToggleIsland();
        _tray.DoubleClick += (_, _) => ToggleIsland();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ToggleIsland();
        };

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

        _tray.ContextMenuStrip.Opening += (_, _) =>
        {
            startup.Checked = _settingsService.IsStartWithWindowsEnabled();
        };

        _tray.ContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[]
        {
            hide,
            focus,
            test,
            chargingAlwaysOn,
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
            if (e.ClickCount == 2) { ResetPosition(); return; }
            if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
            double l = Left, t = Top;
            DragMove();
            SnapToEdge();
            if (Left == l && Top == t && _mode == Mode.Media && _view == (int)IslandLogic.ExpandedView.Music)
                OpenSource();
        };

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
            Position();
            _backdrop = new BackdropBlur(this, IslandShape, IslandTint);
            SetBackdrop(_settingsService.UseBackdropBlur, save: false);
            _settingsService.EnsureStartupPathUpToDate();
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
        };

        Closed += (_, _) =>
        {
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            _clock.Stop();
            _trim.Stop();
            _privacyTimer.Stop();
            _revert.Stop();
            _toastTimer.Stop();
            _watchTimer.Stop();

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
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateClock);
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Position);

    private void MakeOverlay()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = new IntPtr(exStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, newStyle);
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

    private void Position()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    private void Go(Mode mode, double holdSeconds = 0)
    {
        _revert.Stop();
        var prev = _mode;
        bool timerNeedsReanim = mode == Mode.Timer &&
            IslandLogic.ShouldReanimateTimerPill(ShapeGeo.Rect.Width, _playing, _privacyActive);

        if (mode != _mode || timerNeedsReanim)
        {
            _mode = mode;
            if (mode != Mode.Media)
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
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void Animate(Mode mode)
    {
        var (w, h, r) = IslandLogic.GetPillDimensions(mode.ToString(), _playing, _privacyActive, _isCharging);

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

        var to = IslandLogic.CalculateIslandRect(WinW, w, h, _bottom, TopGap);
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

        Fade(IdlePanel,     mode == Mode.Idle);
        Fade(CompactPanel,  mode == Mode.Compact);
        Fade(MediaPanel,    mode == Mode.Media);
        Fade(VolumePanel,   mode == Mode.Volume);
        Fade(NotifyPanel,   mode == Mode.Notify);
        Fade(TimerPanel,    mode == Mode.Timer);
        Fade(ChargingPanel, mode == Mode.Charging);
        UpdateBars();
    }

    private void SetClockFormat(bool use24h)
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
        bool run = _playing && (_mode is Mode.Compact or Mode.Timer) && SystemParameters.ClientAreaAnimation;
        if (run == _barsOn) return;
        _barsOn = run;

        var bars = new[] { B1, B2, B3, B4, TB1, TB2, TB3, TB4 };
        double[] ms = { 420, 560, 480, 640, 420, 560, 480, 640 };
        double[] top = { 1.0, 0.85, 1.0, 0.9, 1.0, 0.85, 1.0, 0.9 };
        for (int i = 0; i < bars.Length; i++)
        {
            if (run)
            {
                var bar = new DoubleAnimation(0.2, top[i], TimeSpan.FromMilliseconds(ms[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase()
                };
                Timeline.SetDesiredFrameRate(bar, 30);
                bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, bar);
            }
            else
            {
                bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                bars[i].ScaleY = 0.4;
            }
        }
    }

    // Panel swap as a gsap timeline: outgoing fades fast, incoming fades + scales in with a small overshoot.
    private static void Fade(UIElement panel, bool show)
    {
        panel.IsHitTestVisible = show;
        var scale = (ScaleTransform)panel.RenderTransform;
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
                Fx.Reveal(MediaTop, dy, delay);
                var ctl = new System.Collections.Generic.List<FrameworkElement>();
                foreach (var c in MediaCtl.Children) if (c is FrameworkElement fe) ctl.Add(fe);
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
        foreach (var p in new FrameworkElement[] { IdlePanel, CompactPanel, MediaPanel, VolumePanel, NotifyPanel, TimerPanel, ChargingPanel })
        {
            p.VerticalAlignment = _bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            p.Margin = _bottom ? new Thickness(0, 0, 0, TopGap) : new Thickness(0, TopGap, 0, 0);
        }
    }

    private void ApplyView()
    {
        ClockView.Visibility = _view == (int)IslandLogic.ExpandedView.Clock ? Visibility.Visible : Visibility.Hidden;
        MediaTop.Visibility = MediaCtl.Visibility = _view == (int)IslandLogic.ExpandedView.Music ? Visibility.Visible : Visibility.Hidden;
        TimerView.Visibility = _view == (int)IslandLogic.ExpandedView.Timer ? Visibility.Visible : Visibility.Hidden;
        ClipboardView.Visibility = _view == (int)IslandLogic.ExpandedView.Clipboard ? Visibility.Visible : Visibility.Hidden;
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
        _rot = 0; _bottom = false; SetAnchor();
        Root.LayoutTransform = Transform.Identity;
        IdleText.LayoutTransform = Transform.Identity;
        Width = 440; Height = 160;
        Position();
        UpdateClock();
        Animate(_mode);
    }

    private void SnapToEdge()
    {
        double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight;
        double d = ShapeGeo.Rect.Height / 2;
        double cx = _rot == 0 ? Left + 220 : Left + (_rot < 0 ? 6 + d : 154 - d);
        double cy = _rot == 0 ? (_bottom ? Top + 154 - d : Top + 6 + d) : Top + 220;
        double min = Math.Min(Math.Min(cy, H - cy), Math.Min(cx, W - cx));
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
        _view = IslandLogic.GetInitialExpandedView(_playing);
        ApplyView();
        Go(Mode.Media);
    }

    private void OnIslandLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hover = false;
        Go(_mode, 0.8);
    }
}
