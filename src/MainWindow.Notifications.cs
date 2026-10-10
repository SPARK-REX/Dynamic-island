// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class MainWindow
{
    private bool _privacyActive;

    private void CheckPrivacy()
    {
        var (mic, cam) = PrivacyService.CheckHardwareAccess();

        MicDot.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        CamDot.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;

        bool show = mic || cam;
        if (show != _privacyActive)
        {
            _privacyActive = show;
            UpdateIdleTextShift();
            double o0 = PrivacyPanel.Opacity, o1 = show ? 1 : 0;
            Gsap.Num(PrivacyPanel, "fade", o0, o1, 0.3, Ease.PowerOut(2), v => PrivacyPanel.Opacity = v);
            Animate(_mode);   // smoothly morph island width to accommodate privacy dot
        }
    }

    private void InitFocusTimer()
    {
        _timerService.Tick += left => Dispatcher.InvokeAsync(() =>
        {
            TimerRun.Text = IslandLogic.FormatTimer(left);
            RefreshTimerView();
        });

        _timerService.Completed += minutes => Dispatcher.InvokeAsync(() =>
        {
            SyncRest();
            RefreshTimerView();
            Notify("Focus session complete", $"{minutes} min focus session finished", "Timer", 8);
        });
    }

    private void StartTimer(int minutes)
    {
        _timerService.Start(minutes);
        TimerRun.Text = IslandLogic.FormatTimer(_timerService.Duration);
        SyncRest();
    }

    private void CancelTimer()
    {
        _timerService.Cancel();
        SyncRest();
        RefreshTimerView();
    }

    private int? AskMinutes() => TimerDialog.Ask(_setMin);

    /// <summary>Expand into a notification card for a few seconds, then shrink back.</summary>
    private void Notify(string title, string text, string? app = null, double hold = 5)
    {
        NotifyTitle.Text = string.IsNullOrWhiteSpace(title) ? "Notification" : title;
        NotifyText.Text = string.IsNullOrWhiteSpace(text) ? "New message received" : text;

        var (r, g, b, glyph) = IslandLogic.GetBadgeStyle(app, title);
        NotifyBadge.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        NotifyIcon.Text = glyph;

        Go(Mode.Notify, hold);
    }

    private async Task InitNotificationEngineAsync()
    {
        _notificationService.NotificationReceived += item =>
            Dispatcher.InvokeAsync(() => Notify(item.Title, item.Body, item.App));

        await _notificationService.InitializeAsync();
    }

    private async Task PollNativeNotificationsAsync()
    {
        await _notificationService.PollNativeNotificationsAsync();
    }

    private void PollToast()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _notificationService.PollToast(hwnd);
    }
}
