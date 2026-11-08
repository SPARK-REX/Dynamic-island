// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// First-run or settings popup prompting the user for their Google Gemini API key.
/// Saves key to %APPDATA%\DynamicIsland\gemini.key and base directory.
/// </summary>
public partial class ApiKeyWindow : Window
{
    private readonly Action<string>? _onKeySaved;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_DRAGMOVE = 0xF012;

    public ApiKeyWindow(Action<string>? onKeySaved = null)
    {
        _onKeySaved = onKeySaved;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            ApplyDarkMode();
            string? current = VoiceAssistantService.ReadGeminiApiKey();
            if (!string.IsNullOrWhiteSpace(current))
            {
                TxtApiKey.Text = current;
                TxtApiKey.SelectAll();
            }
            TxtApiKey.Focus();
        };
    }

    private void ApplyDarkMode()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    private void OnTitlebarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, WM_SYSCOMMAND, (IntPtr)SC_DRAGMOVE, IntPtr.Zero);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnSkipClick(object sender, RoutedEventArgs e) => Close();

    private void OnGetKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Program.Log($"Failed to open AI Studio link: {ex.Message}");
        }
    }

    private void OnApiKeyTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LblStatus.Visibility = Visibility.Collapsed;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OnSaveClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string key = (TxtApiKey.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            LblStatus.Text = "Please enter a valid Gemini API key.";
            LblStatus.Visibility = Visibility.Visible;
            return;
        }

        VoiceAssistantService.SaveGeminiApiKey(key);
        _onKeySaved?.Invoke(key);
        Close();
    }
}

