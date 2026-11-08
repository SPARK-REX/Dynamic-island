// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Native WPF Settings window (no WebView2): renders instantly like any other app's settings.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly MainWindow _mainWindow;
    private readonly VoiceAssistantService _voiceService;
    private bool _loading;                 // true while pushing settings into the controls (suppresses events)
    private long _lastMeterTick;
    private HotkeyPickerWindow? _hotkeyPickerWindow;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private const int WM_GETMINMAXINFO = 0x0024;
    private bool _fullscreen;                              // true = cover taskbar (F11), false = fill work area
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_DRAGMOVE = 0xF012;

    public SettingsWindow(SettingsService settingsService, MainWindow mainWindow, VoiceAssistantService voiceService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));

        InitializeComponent();

        // Ensure the default (General) pane is visible regardless of nav Checked event timing.
        // NavGeneral.IsChecked="True" fires during InitializeComponent before the panes exist,
        // so OnNavChecked returns early and never syncs visibility on first load.
        PaneGeneral.Visibility = Visibility.Visible;
        PaneVoice.Visibility = Visibility.Collapsed;
        PaneDisplay.Visibility = Visibility.Collapsed;
        PaneAbout.Visibility = Visibility.Collapsed;

        try
        {
            var assetIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app.ico");
            if (File.Exists(assetIco))
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(assetIco, UriKind.Absolute));
            }
        }
        catch (Exception ex)
        {
            Program.Log($"SettingsWindow icon init warning: {ex.Message}");
        }

        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += OnIsVisibleChanged;
        StateChanged += OnWindowStateChanged;
        Closed += OnClosed;

        _voiceService.AudioLevelChanged += OnAudioLevelChanged;

        LoadSettingsIntoUI();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

            // Windows 11 rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUNDED = 2)
            int cornerPref = 2;
            DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));

            // Windows 10/11 dark window frame attribute (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch (Exception ex)
        {
            Program.Log($"DWM window styling error: {ex.Message}");
        }
    }

    /// <summary>Real maximize: fill monitor work area (or whole monitor in fullscreen) instead of WPF's borderless overflow.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        try
        {
            var mi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
            {
                var area = _fullscreen ? info.rcMonitor : info.rcWork;
                mi.ptMaxPosition = new POINT { x = area.left - info.rcMonitor.left, y = area.top - info.rcMonitor.top };
                mi.ptMaxSize = new POINT { x = area.right - area.left, y = area.bottom - area.top };
                mi.ptMaxTrackSize = mi.ptMaxSize;
            }
            var src = PresentationSource.FromVisual(this);
            double dx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1, dy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            mi.ptMinTrackSize = new POINT { x = (int)(MinWidth * dx), y = (int)(MinHeight * dy) };
            Marshal.StructureToPtr(mi, lParam, true);
            handled = true;
        }
        catch (Exception ex) { Program.Log($"WM_GETMINMAXINFO error: {ex.Message}"); }
        return IntPtr.Zero;
    }

    private void SetFullscreen(bool on)
    {
        _fullscreen = on;
        if (on)
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // force min/max recalculation
            WindowState = WindowState.Maximized;
        }
        else WindowState = WindowState.Normal;
    }

    /// <summary>Create HWND while hidden so first Show() is instant.</summary>
    public void PreWarm() => new WindowInteropHelper(this).EnsureHandle();

    // ---------------------------------------------------------------- window chrome

    private void OnTitlebarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) PerformWindowDrag();
    }

    private void OnMinClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxClick(object sender, RoutedEventArgs e)
    {
        _fullscreen = false;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        BtnMax.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) != 0))
        {
            SetFullscreen(!(WindowState == WindowState.Maximized && _fullscreen));
            e.Handled = true;
        }
    }

    private void PerformWindowDrag()
    {
        try
        {
            if (WindowState == WindowState.Maximized)
            {
                var mousePos = System.Windows.Forms.Cursor.Position;
                WindowState = WindowState.Normal;
                Left = Math.Max(0, mousePos.X - (Width / 2));
                Top = Math.Max(0, mousePos.Y - 20);
            }

            var helper = new WindowInteropHelper(this);
            ReleaseCapture();
            SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)SC_DRAGMOVE, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Program.Log($"PerformWindowDrag error: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- visibility / audio meter

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            RunMeter(_voiceService.StartAudioMeter); // mic open is slow: keep UI thread free, but keep start/stop ordered
            LoadSettingsIntoUI();
            LoadDevicesAsync();
        }
        else
        {
            RunMeter(_voiceService.StopAudioMeter);
        }
    }

    private Task _meterChain = Task.CompletedTask;
    private void RunMeter(Action a) => _meterChain = _meterChain.ContinueWith(_ => { try { a(); } catch (Exception ex) { Program.Log(ex); } }, TaskScheduler.Default);

    private void OnAudioLevelChanged(float level)
    {
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastMeterTick).TotalMilliseconds < 35) return;
        _lastMeterTick = now;

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (IsVisible) MicMeter.Value = Math.Clamp(level * 100f, 0, 100);
            }
            catch { }
        });
    }

    private bool _loadingDevices;
    private void LoadDevicesAsync()
    {
        if (_loadingDevices) return;
        _loadingDevices = true;
        Task.Run(() =>
        {
            try
            {
                var list = VoiceAssistantService.GetAudioInputDevices();
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _loading = true;
                        CmbMicDevice.Items.Clear();
                        foreach (var d in list)
                        {
                            CmbMicDevice.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
                        }
                        string cur = _settingsService.MicrophoneDeviceId;
                        ComboBoxItem? match = null;
                        foreach (ComboBoxItem item in CmbMicDevice.Items)
                        {
                            if ((string?)item.Tag == cur) { match = item; break; }
                        }
                        CmbMicDevice.SelectedItem = match ?? (CmbMicDevice.Items.Count > 0 ? CmbMicDevice.Items[0] : null);
                    }
                    finally { _loading = false; }
                    _loadingDevices = false;
                });
            }
            catch (Exception ex) { _loadingDevices = false; Program.Log(ex); }
        });
    }

    // ---------------------------------------------------------------- load state into UI

    private void LoadSettingsIntoUI()
    {
        _loading = true;
        try
        {
            TglAlwaysOnTop.IsChecked = _settingsService.AlwaysOnTopFullscreen;
            TglStartWithWindows.IsChecked = _settingsService.IsStartWithWindowsEnabled();
            TglCharging.IsChecked = _settingsService.AlwaysShowChargingIndicator;
            CheckByTag(new[] { RadFocus15, RadFocus25, RadFocus45, RadFocus60 }, _settingsService.FocusTimerDefaultMinutes.ToString(CultureInfo.InvariantCulture));

            TglPushToTalk.IsChecked = _settingsService.PushToTalkEnabled;
            HotkeyBadge.Text = _settingsService.PushToTalkShortcutName;

            TglWakeWord.IsChecked = _settingsService.WakeWordEnabled;
            CheckByTag(new[] { RadWakeHeyNixi, RadWakeNixi, RadWakeIsland }, _settingsService.WakePhrase);

            string provider = _settingsService.ModelProvider;
            CheckByTag(new[] { RadProviderCloud, RadProviderLocal }, provider);
            LocalModelPanel.Visibility = provider == "local" ? Visibility.Visible : Visibility.Collapsed;
            TxtLocalModelName.Text = _settingsService.LocalModelName;
            TxtLocalModelEndpoint.Text = _settingsService.LocalModelEndpoint;

            SldMic.Value = _settingsService.MicrophoneSensitivity;
            LblMicVal.Text = _settingsService.MicrophoneSensitivity + "%";

            CheckByTag(new[] { RadThemeGlassy, RadThemeBlack }, _settingsService.UseBackdropBlur ? "glassy" : "pure-black");
            CheckByTag(new[] { RadClock12, RadClock24 }, _settingsService.Use24HourClock ? "24" : "12");

            TglMascot.IsChecked = _settingsService.MascotEnabled;
            SelectComboByTag(CmbAccessory, _settingsService.MascotAccessory);
            TglDeepSleep.IsChecked = _settingsService.MascotDeepSleep;
            TglMascotNeverSleep.IsChecked = _settingsService.MascotNeverSleep;
            TglMascotNeverSleepDisplay.IsChecked = _settingsService.MascotNeverSleep;
            TglMascotPrivacy.IsChecked = _settingsService.MascotPrivacyIndicatorsEnabled;
            TglMascotPrivacyDisplay.IsChecked = _settingsService.MascotPrivacyIndicatorsEnabled;

            bool mascotOn = _settingsService.MascotEnabled;
            TglMascotPrivacy.IsEnabled = mascotOn;
            TglMascotPrivacyDisplay.IsEnabled = mascotOn;

            SelectComboByTag(CmbListenStyle, _settingsService.MascotListenStyle);
            UpdateEyeColorIndicator(_settingsService.MascotEyeColor);

            CheckByTag(new[] { RadScale80, RadScale100, RadScale120 }, _settingsService.IslandScale.ToString("0.0#", CultureInfo.InvariantCulture));
            CheckByTag(new[] { RadPosLeft, RadPosCenter, RadPosRight }, _settingsService.IslandPosition);

            TglTelemetry.IsChecked = _settingsService.AnonymousTelemetryEnabled;
        }
        finally { _loading = false; }
    }

    private static void CheckByTag(RadioButton[] buttons, string? tag)
    {
        foreach (var b in buttons) b.IsChecked = string.Equals(b.Tag?.ToString(), tag, StringComparison.Ordinal);
    }

    private static void SelectComboByTag(ComboBox combo, string? tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal)) { combo.SelectedItem = item; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    // ---------------------------------------------------------------- nav

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        // Checked fires during InitializeComponent (NavGeneral has IsChecked="True" in XAML) before the panes exist
        if (PaneGeneral == null || _loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        PaneGeneral.Visibility = rb == NavGeneral ? Visibility.Visible : Visibility.Collapsed;
        PaneVoice.Visibility = rb == NavVoice ? Visibility.Visible : Visibility.Collapsed;
        PaneDisplay.Visibility = rb == NavDisplay ? Visibility.Visible : Visibility.Collapsed;
        PaneAbout.Visibility = rb == NavAbout ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- general tab

    private void OnAlwaysOnTopClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglAlwaysOnTop.IsChecked == true;
        _settingsService.SetAlwaysOnTopFullscreen(v);
        _mainWindow.EnforceTopmost();
    }

    private void OnStartWithWindowsClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglStartWithWindows.IsChecked == true;
        if (!_settingsService.SetStartWithWindows(v))
        {
            // failed: put the switch back to real state
            _loading = true;
            TglStartWithWindows.IsChecked = _settingsService.IsStartWithWindowsEnabled();
            _loading = false;
        }
    }

    private void OnChargingClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglCharging.IsChecked == true;
        _settingsService.SetAlwaysShowChargingIndicator(v);
        _mainWindow.SetAlwaysCharging(v);
    }

    private void OnFocusDurChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        if (int.TryParse(rb.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m))
        {
            _settingsService.SetFocusTimerDefaultMinutes(m);
            _mainWindow.SetFocusMinutes(m);
        }
    }

    private void OnFocusCustomClick(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.OpenCustomFocusTimer() is int customMinutes)
        {
            _settingsService.SetFocusTimerDefaultMinutes(customMinutes);
            _mainWindow.SetFocusMinutes(customMinutes);
            _loading = true;
            CheckByTag(new[] { RadFocus15, RadFocus25, RadFocus45, RadFocus60 }, customMinutes.ToString(CultureInfo.InvariantCulture));
            _loading = false;
        }
    }

    private void OnFocusStartClick(object sender, RoutedEventArgs e) => _mainWindow.StartFocusTimerFromSettings();

    private void OnTestNotifyClick(object sender, RoutedEventArgs e) =>
        _mainWindow.Notify("Dynamic Island", "Hello! This is how a notification looks.");

    // ---------------------------------------------------------------- voice tab

    private void OnPushToTalkClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglPushToTalk.IsChecked == true;
        _settingsService.SetPushToTalkEnabled(v);
        _voiceService.SetPushToTalkEnabled(v);
    }

    private void OnPresetKeyClick(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not Button b || b.Tag is not string tag) return;
        var parts = tag.Split(';');
        if (parts.Length != 3) return;
        string name = parts[0];
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vk)) vk = 0x14;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mod)) mod = 0;
        ApplyPushToTalkKey(name, vk, mod);
    }

    private void ApplyPushToTalkKey(string name, int vk, int mod)
    {
        _settingsService.SetPushToTalkShortcut(name, vk, mod);
        _voiceService.SetPushToTalkShortcut(name, vk, mod);
        HotkeyBadge.Text = name;
    }

    private void OnCustomKeyClick(object sender, RoutedEventArgs e) => OpenHotkeyPicker();

    private void OnWakeWordClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglWakeWord.IsChecked == true;
        _settingsService.SetWakeWordEnabled(v);
        _voiceService.SetWakeWordEnabled(v);
    }

    private void OnWakePhraseChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        string phrase = rb.Tag?.ToString() ?? "Hey Nixi";
        _settingsService.SetWakePhrase(phrase);
        _voiceService.SetWakePhrase(phrase);
    }

    private void OnProviderChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        string provider = rb.Tag?.ToString() ?? "cloud";
        _settingsService.SetModelProvider(provider);
        _voiceService.SetModelProvider(provider);
        LocalModelPanel.Visibility = provider == "local" ? Visibility.Visible : Visibility.Collapsed;
        if (provider == "local")
        {
            // Start ollama serve / pull the model in the background, then report status.
            _ = Task.Run(async () =>
            {
                string status = await _voiceService.EnsureLocalModelReadyAsync().ConfigureAwait(false);
                _ = Dispatcher.InvokeAsync(() => _mainWindow.Notify("Local model", status, "VoiceAssistant", 9.0));
            });
        }
    }

    private void OnApiKeySetupClick(object sender, RoutedEventArgs e)
    {
        _mainWindow.OpenApiKeySetup();
    }

    private void OnLocalModelNameCommit(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string v = TxtLocalModelName.Text;
        _settingsService.SetLocalModelName(v);
        _voiceService.SetLocalModel(v);
    }

    private void OnLocalModelNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnLocalModelNameCommit(sender, e); e.Handled = true; }
    }

    private void OnLocalModelEndpointCommit(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string v = TxtLocalModelEndpoint.Text;
        _settingsService.SetLocalModelEndpoint(v);
        _voiceService.SetLocalEndpoint(v);
    }

    private void OnLocalModelEndpointKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnLocalModelEndpointCommit(sender, e); e.Handled = true; }
    }

    private void OnMicDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbMicDevice.SelectedItem is ComboBoxItem item && item.Tag is string id)
        {
            _settingsService.SetMicrophoneDevice(id);
            _voiceService.SetMicrophoneDevice(id);
        }
    }

    private void OnMicSensChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int s = (int)Math.Round(e.NewValue);
        LblMicVal.Text = s + "%";
        if (_loading) return;
        _settingsService.SetMicrophoneSensitivity(s);
        _voiceService.SetMicrophoneSensitivity(s);
    }

    // ---------------------------------------------------------------- display tab

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        _mainWindow.SetBackdrop(rb.Tag?.ToString() == "glassy");
    }

    private void OnClockChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        _mainWindow.SetClockFormat(rb.Tag?.ToString() == "24");
    }

    private void OnMascotClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = TglMascot.IsChecked == true;
        _settingsService.SetMascotEnabled(v);
        _mainWindow.SetMascotEnabled(v);
        TglMascotPrivacy.IsEnabled = v;
        TglMascotPrivacyDisplay.IsEnabled = v;
    }

    private void OnMascotNeverSleepClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = (sender as CheckBox)?.IsChecked == true;
        _settingsService.SetMascotNeverSleep(v);
        _mainWindow.SetMascotNeverSleep(v);
        _loading = true;
        TglMascotNeverSleep.IsChecked = v;
        TglMascotNeverSleepDisplay.IsChecked = v;
        _loading = false;
    }

    private void OnMascotPrivacyClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool v = (sender as CheckBox)?.IsChecked == true;
        _settingsService.SetMascotPrivacyIndicatorsEnabled(v);
        _mainWindow.SetMascotPrivacyIndicatorsEnabled(v);
        _loading = true;
        TglMascotPrivacy.IsChecked = v;
        TglMascotPrivacyDisplay.IsChecked = v;
        _loading = false;
    }

    private void OnAccessoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbAccessory.SelectedItem is ComboBoxItem item)
            _settingsService.SetMascotAccessory(item.Tag?.ToString() ?? "none");
    }

    private void OnDeepSleepClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settingsService.SetMascotDeepSleep(TglDeepSleep.IsChecked == true);
    }

    private void OnListenStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbListenStyle.SelectedItem is ComboBoxItem item)
            _settingsService.SetMascotListenStyle(item.Tag?.ToString() ?? "glow");
    }

    private void OnEyeColorClick(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not Button b || b.Tag is not string hex) return;
        ApplyEyeColor(hex);
    }

    private void OnColorPaletteClick(object sender, RoutedEventArgs e)
    {
        PalettePopup.PlacementTarget = BtnColorPalette;
        PalettePopup.IsOpen = !PalettePopup.IsOpen;
    }

    private void OnPaletteColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex)
        {
            ApplyEyeColor(hex);
            PalettePopup.IsOpen = false;
        }
    }

    private void OnSystemColorPickerClick(object sender, RoutedEventArgs e)
    {
        PalettePopup.IsOpen = false;
        try
        {
            var cur = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settingsService.MascotEyeColor);
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B)
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                ApplyEyeColor(hex);
            }
        }
        catch (Exception ex)
        {
            Program.Log($"OnSystemColorPickerClick error: {ex.Message}");
        }
    }

    private void ApplyEyeColor(string hexColor)
    {
        _settingsService.SetMascotEyeColor(hexColor);
        _mainWindow.SetMascotEyeColor(hexColor);
        UpdateEyeColorIndicator(hexColor);
    }

    private void UpdateEyeColorIndicator(string hexColor)
    {
        try
        {
            if (CurrentEyeColorSwatch != null)
            {
                var brush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hexColor)!;
                CurrentEyeColorSwatch.Background = brush;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"UpdateEyeColorIndicator error: {ex.Message}");
        }
    }

    private void OnScaleChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        if (double.TryParse(rb.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
        {
            _settingsService.SetIslandScale(scale);
            _mainWindow.SetIslandScale(scale);
        }
    }

    private void OnPositionChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.IsChecked != true) return;
        string pos = rb.Tag?.ToString() ?? "center";
        _settingsService.SetIslandPosition(pos);
        _mainWindow.SetIslandPosition(pos);
    }

    // ---------------------------------------------------------------- about tab

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => LblUpdateStatus.Text = "Up to date (v1.0.0)";

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string url) return;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Program.Log($"Failed to open link: {ex.Message}");
            }
        }
    }

    private void OnTelemetryClick(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settingsService.SetAnonymousTelemetryEnabled(TglTelemetry.IsChecked == true);
    }

    // ---------------------------------------------------------------- hotkey picker popup

    /// <summary>Open (or re-focus) the separate hotkey picker popup window.</summary>
    private void OpenHotkeyPicker()
    {
        try
        {
            if (_hotkeyPickerWindow == null)
            {
                _hotkeyPickerWindow = new HotkeyPickerWindow(_settingsService, _voiceService, this)
                {
                    Owner = this
                };
                _hotkeyPickerWindow.Closed += (_, _) => _hotkeyPickerWindow = null;
            }
            if (_hotkeyPickerWindow.WindowState == WindowState.Minimized)
                _hotkeyPickerWindow.WindowState = WindowState.Normal;
            _hotkeyPickerWindow.Show();
            _hotkeyPickerWindow.Activate();
        }
        catch (Exception ex)
        {
            Program.Log($"OpenHotkeyPicker error: {ex.Message}");
        }
    }

    /// <summary>Push a hotkey chosen in the popup picker back to the settings page UI.</summary>
    public void NotifyPushToTalkKeyChanged(string name, int vk, int mod)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try { HotkeyBadge.Text = name; }
            catch (Exception ex) { Program.Log($"NotifyPushToTalkKeyChanged error: {ex.Message}"); }
        });
    }

    // ---------------------------------------------------------------- lifecycle

    public bool IsShuttingDown { get; set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (IsShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _voiceService.AudioLevelChanged -= OnAudioLevelChanged;
        _voiceService.StopAudioMeter();
        try { _hotkeyPickerWindow?.Close(); } catch { }
    }
}
