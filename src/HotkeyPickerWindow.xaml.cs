// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Separate native popup window for picking the push-to-talk hotkey.
/// Applies the chosen binding through the shared services and reports it
/// back to the owning SettingsWindow, then closes.
/// </summary>
public partial class HotkeyPickerWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly VoiceAssistantService _voiceService;
    private readonly SettingsWindow _owner;

    private bool _capturing;
    private int? _heldMod;                 // modifier pressed first in a two-key combo
    private string _pendingName = "";
    private int _pendingVk = 0x14;
    private int _pendingMod;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_DRAGMOVE = 0xF012;

    public HotkeyPickerWindow(SettingsService settingsService, VoiceAssistantService voiceService, SettingsWindow owner)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        InitializeComponent();

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
            Program.Log($"HotkeyPickerWindow icon init warning: {ex.Message}");
        }

        TxtCurrent.Text = _settingsService.PushToTalkShortcutName;
        if (_settingsService.PushToTalkModifierKey != 0) RadModeCombo.IsChecked = true;
        else RadModeSingle.IsChecked = true;
        UpdateStatusPrompt();

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // Windows 11 rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUNDED = 2)
            int cornerPref = 2;
            DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));

            // Windows 10/11 dark window frame attribute (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch (Exception ex)
        {
            Program.Log($"HotkeyPickerWindow DWM window styling error: {ex.Message}");
        }
    }

    private void OnTitlebarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try
        {
            var helper = new WindowInteropHelper(this);
            ReleaseCapture();
            SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)SC_DRAGMOVE, IntPtr.Zero);
        }
        catch (Exception ex) { Program.Log($"HotkeyPickerWindow drag error: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- selection

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        _heldMod = null;
        UpdateStatusPrompt();
    }

    private void UpdateStatusPrompt()
    {
        bool isCombo = RadModeCombo?.IsChecked == true;
        if (TxtStatus == null) return;
        if (_capturing)
        {
            TxtStatus.Text = isCombo
                ? "Listening for combo… press 2 keys: first a modifier (Ctrl, Alt, Shift, or Win), then the second key."
                : "Listening… press 1 key to set it.";
        }
        else
        {
            TxtStatus.Text = isCombo
                ? "Turn on capture, then press 2 keys (modifier + key)."
                : "Turn on capture, then press 1 key.";
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        var parts = tag.Split(';');
        if (parts.Length != 3) return;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vk)) vk = 0x14;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mod)) mod = 0;

        if (mod != 0) RadModeCombo.IsChecked = true;
        else RadModeSingle.IsChecked = true;

        SetPending(parts[0], vk, mod);
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        _capturing = TglCapture.IsChecked == true;
        _heldMod = null;
        UpdateStatusPrompt();
        if (_capturing) Activate();
    }

    private void OnPickerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_capturing)
            {
                _capturing = false;
                _heldMod = null;
                TglCapture.IsChecked = false;
                UpdateStatusPrompt();
            }
            else Close();
            e.Handled = true;
            return;
        }
        if (!_capturing) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool isCombo = RadModeCombo.IsChecked == true;

        if (!isCombo)
        {
            // SINGLE KEY MODE: pressing any single key immediately captures it (1 key press)
            int vk;
            string name;
            if (IsModifierKey(key))
            {
                vk = ModifierVk(key);
                name = $"[{ModifierName(vk)}]";
            }
            else
            {
                vk = KeyInterop.VirtualKeyFromKey(key);
                name = $"[{NormalizeKeyName(key)}]";
            }
            _heldMod = null;
            SetPending(name, vk, 0);
            TxtStatus.Text = $"Captured single key {name}. Press Apply to save.";
            e.Handled = true;
            return;
        }

        // COMBO MODE: requires 2 keys (modifier + main key)
        if (IsModifierKey(key))
        {
            _heldMod = ModifierVk(key);
            TxtPreview.Text = $"[{ModifierName(_heldMod.Value)} + ...]";
            TxtStatus.Text = $"Key 1 of 2 ({ModifierName(_heldMod.Value)}) recorded — now press the second key.";
            e.Handled = true;
            return;
        }

        int mod = _heldMod ?? ModifierFromKeyboard();
        if (mod == 0)
        {
            TxtStatus.Text = "In Combo mode, press a modifier key first (Ctrl, Alt, Shift, or Win), then press the second key.";
            e.Handled = true;
            return;
        }

        int mainVk = KeyInterop.VirtualKeyFromKey(key);
        _heldMod = null;
        string comboName = BuildName(mod, key);
        SetPending(comboName, mainVk, mod);
        TxtStatus.Text = $"Captured combo {comboName}. Press Apply to save.";
        e.Handled = true;
    }

    private void SetPending(string name, int vk, int mod)
    {
        _pendingName = name;
        _pendingVk = vk;
        _pendingMod = mod;
        TxtPreview.Text = name;
        BtnApply.IsEnabled = true;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pendingName)) return;
        _settingsService.SetPushToTalkShortcut(_pendingName, _pendingVk, _pendingMod);
        _voiceService.SetPushToTalkShortcut(_pendingName, _pendingVk, _pendingMod);
        _owner.NotifyPushToTalkKeyChanged(_pendingName, _pendingVk, _pendingMod);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- key mapping helpers

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static int ModifierVk(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => 0x11,   // VK_CONTROL
        Key.LeftAlt or Key.RightAlt => 0x12,     // VK_MENU
        Key.LeftShift or Key.RightShift => 0x10, // VK_SHIFT
        Key.LWin or Key.RWin => 0x5B,            // VK_LWIN
        _ => 0
    };

    private static int ModifierFromKeyboard()
    {
        var m = Keyboard.Modifiers;
        if ((m & ModifierKeys.Control) != 0) return 0x11;
        if ((m & ModifierKeys.Alt) != 0) return 0x12;
        if ((m & ModifierKeys.Shift) != 0) return 0x10;
        if ((m & ModifierKeys.Windows) != 0) return 0x5B;
        return 0;
    }

    private static string ModifierName(int modVk) => modVk switch
    {
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x10 => "Shift",
        0x5B => "Win",
        _ => ""
    };

    private static string NormalizeKeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(CultureInfo.InvariantCulture),
        Key.Space => "Space",
        Key.Capital => "Caps Lock",
        Key.Oem3 => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem1 => ";",
        Key.Oem2 => "/",
        Key.Oem7 => "'",
        Key.Oem5 => "\\",
        Key.Oem4 => "[",
        Key.Oem6 => "]",
        Key.Escape => "Esc",
        _ => key.ToString()
    };

    private static string BuildName(int modVk, Key key)
    {
        string keyName = NormalizeKeyName(key);
        return modVk != 0 ? $"[{ModifierName(modVk)} + {keyName}]" : $"[{keyName}]";
    }
}
