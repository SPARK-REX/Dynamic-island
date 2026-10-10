// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Services;

public sealed record NotificationItem(string Title, string Body, string App);

/// <summary>
/// Domain service coordinating Windows Toast notifications via UWP UserNotificationListener
/// and fallback UI Automation scraping for custom messaging desktop apps.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemsettings", "applicationframehost", "searchhost", "searchapp",
        "startmenuexperiencehost", "textinputhost", "ctfmon", "lockapp",
        "taskmgr", "dynamicisland", "devenv", "code", "antigravity-ide",
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi",
        "powershell", "cmd"
    };

    private static readonly HashSet<string> ExcludedExplorerClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CabinetWClass", "ExploreWClass", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "DV2ControlHost"
    };

    private static readonly string[] KnownMessagingProcesses =
    {
        "telegram", "whatsapp", "discord", "slack", "signal", "teams", "ms-teams", "skype", "wechat", "outlook", "hxoutlook", "thunderbird"
    };

    private readonly ConcurrentDictionary<string, DateTime> _seenToasts = new();
    private readonly HashSet<uint> _seenNotificationIds = new();
    private readonly Queue<uint> _seenNotificationQueue = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.Now;

    private UserNotificationListener? _userNotificationListener;
    private int _isScanningWindows;
    private bool _disposed;

    public event Action<NotificationItem>? NotificationReceived;

    public async Task InitializeAsync()
    {
        if (_disposed) return;
        try
        {
            _userNotificationListener = UserNotificationListener.Current;
            if (_userNotificationListener != null)
            {
                var status = await _userNotificationListener.RequestAccessAsync();
                if (status == UserNotificationListenerAccessStatus.Allowed)
                {
                    var existing = await _userNotificationListener.GetNotificationsAsync(NotificationKinds.Toast);
                    if (existing != null)
                    {
                        foreach (var n in existing)
                        {
                            _seenNotificationIds.Add(n.Id);
                            _seenNotificationQueue.Enqueue(n.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    public async Task PollNativeNotificationsAsync()
    {
        if (_disposed || _userNotificationListener == null) return;
        try
        {
            var toasts = await _userNotificationListener.GetNotificationsAsync(NotificationKinds.Toast);
            if (toasts == null || _disposed) return;

            foreach (var n in toasts)
            {
                if (!_seenNotificationIds.Contains(n.Id))
                {
                    _seenNotificationIds.Add(n.Id);
                    _seenNotificationQueue.Enqueue(n.Id);
                    if (_seenNotificationQueue.Count > 300)
                    {
                        uint oldId = _seenNotificationQueue.Dequeue();
                        _seenNotificationIds.Remove(oldId);
                    }

                    if (n.CreationTime < _startTime || (DateTimeOffset.Now - n.CreationTime).TotalSeconds > 15)
                        continue;

                    var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (binding == null) continue;

                    var texts = binding.GetTextElements()?
                        .Select(t => t.Text?.Trim() ?? "")
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();

                    if (texts == null || texts.Count == 0) continue;

                    string defaultApp = "";
                    try { defaultApp = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }

                    var (app, title, body) = IslandLogic.ParseNotification(defaultApp, texts);

                    if (string.IsNullOrWhiteSpace(app))
                    {
                        try
                        {
                            if (n.AppInfo?.Id is string id)
                            {
                                if (id.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) app = "Telegram";
                                else if (id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) app = "WhatsApp";
                                else if (id.Contains("Discord", StringComparison.OrdinalIgnoreCase)) app = "Discord";
                                else if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) app = "Spotify";
                            }
                        }
                        catch { }
                    }

                    string displayTitle = (!string.IsNullOrEmpty(app) &&
                                           !title.Contains(app, StringComparison.OrdinalIgnoreCase) &&
                                           !title.Contains("·"))
                        ? $"{app} · {title}"
                        : title;

                    NotificationReceived?.Invoke(new NotificationItem(displayTitle, body, app));
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    public void PollToast(IntPtr myHwnd)
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _isScanningWindows, 1, 0) != 0) return;

        try
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _seenToasts)
            {
                if ((now - kvp.Value).TotalSeconds > 25)
                {
                    _seenToasts.TryRemove(kvp.Key, out _);
                }
            }

            var candidates = new List<IntPtr>();

            NativeMethods.EnumWindows((h, _) =>
            {
                if (h == myHwnd || !NativeMethods.IsWindowVisible(h)) return true;

                NativeMethods.GetWindowThreadProcessId(h, out var pid);
                if (pid == (uint)Environment.ProcessId || pid == 0) return true;

                // High-performance process name lookup via cache (no Process.GetProcessById allocation)
                string procName = NativeMethods.GetProcessNameCached(pid);
                if (string.IsNullOrEmpty(procName) || ExcludedProcesses.Contains(procName)) return true;

                var sbClass = new StringBuilder(256);
                NativeMethods.GetClassName(h, sbClass, 256);
                string cls = sbClass.ToString();

                if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && ExcludedExplorerClasses.Contains(cls))
                    return true;

                var sbTitle = new StringBuilder(256);
                NativeMethods.GetWindowText(h, sbTitle, 256);
                string title = sbTitle.ToString();

                NativeMethods.GetWindowRect(h, out var rc);
                int w = rc.Width;
                int hpx = rc.Height;

                bool isShellToast = (procName.Equals("shellexperiencehost", StringComparison.OrdinalIgnoreCase) ||
                                     procName.Equals("actioncenter", StringComparison.OrdinalIgnoreCase) ||
                                     procName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) &&
                                    (cls.Contains("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                                     cls.Contains("ToastView", StringComparison.OrdinalIgnoreCase) ||
                                     cls.Contains("ActionCenter", StringComparison.OrdinalIgnoreCase) ||
                                     cls.Contains("Windows.UI.Input.InputSite", StringComparison.OrdinalIgnoreCase) ||
                                     cls.Contains("Shell_Dialog", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("notification", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("Toast", StringComparison.OrdinalIgnoreCase));

                bool isTelegramPopup = procName.Contains("telegram") && (
                    (w >= 160 && w <= 550 && hpx >= 40 && hpx <= 250) ||
                    (cls.Contains("Qt", StringComparison.OrdinalIgnoreCase) && !title.Equals("Telegram", StringComparison.OrdinalIgnoreCase) && w <= 550 && hpx <= 250)
                );

                bool isMessagingPopup = KnownMessagingProcesses.Any(p => procName.Contains(p)) && (w >= 160 && w <= 550 && hpx >= 40 && hpx <= 250);

                if (isShellToast || isTelegramPopup || isMessagingPopup)
                {
                    candidates.Add(h);
                }
                return true;
            }, IntPtr.Zero);

            if (candidates.Count == 0)
            {
                Interlocked.Exchange(ref _isScanningWindows, 0);
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    if (_disposed) return;
                    foreach (var h in candidates)
                    {
                        try
                        {
                            NativeMethods.GetWindowThreadProcessId(h, out var pid);
                            string procName = NativeMethods.GetProcessNameCached(pid);

                            var elem = AutomationElement.FromHandle(h);
                            if (elem == null) continue;

                            var texts = elem.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                                .Cast<AutomationElement>()
                                .Select(e => e.Current.Name?.Trim() ?? "")
                                .Where(n => !string.IsNullOrWhiteSpace(n) && !IslandLogic.IsJunkNotificationText(n))
                                .ToList();

                            if (texts.Count == 0)
                            {
                                texts = elem.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                                    .Cast<AutomationElement>()
                                    .Select(e => e.Current.Name?.Trim() ?? "")
                                    .Where(n => !string.IsNullOrWhiteSpace(n) && n.Length > 1 && !IslandLogic.IsJunkNotificationText(n))
                                    .Distinct()
                                    .ToList();
                            }

                            if (texts.Count == 0) continue;

                            var (app, title, body) = IslandLogic.ParseNotification(procName, texts);

                            if (string.IsNullOrWhiteSpace(title) || IslandLogic.IsJunkNotificationText(title)) continue;
                            if (IslandLogic.IsJunkNotificationText(body)) body = "";

                            string key = $"window:{app}:{title}:{body}";

                            if (_seenToasts.TryGetValue(key, out var last) && (DateTime.UtcNow - last).TotalSeconds < 4)
                                continue;
                            _seenToasts[key] = DateTime.UtcNow;

                            string displayTitle = (!string.IsNullOrEmpty(app) &&
                                                   !title.Contains(app, StringComparison.OrdinalIgnoreCase) &&
                                                   !title.Contains("·"))
                                ? $"{app} · {title}"
                                : title;

                            if (!_disposed)
                            {
                                NotificationReceived?.Invoke(new NotificationItem(displayTitle, body, app));
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.Log(ex);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isScanningWindows, 0);
                }
            });
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            Interlocked.Exchange(ref _isScanningWindows, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _seenToasts.Clear();
        _seenNotificationIds.Clear();
        _seenNotificationQueue.Clear();
        _userNotificationListener = null;
    }
}
