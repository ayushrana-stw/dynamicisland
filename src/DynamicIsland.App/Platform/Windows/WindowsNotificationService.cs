using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Notifications;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.App.Platform.Windows;

/// <summary>
/// Reads toast notifications from every app (Teams, Outlook, WhatsApp, browsers…) through
/// <see cref="UserNotificationListener"/>. Windows only allows this for apps with package identity,
/// which <c>scripts/register-package.ps1</c> provides, and only after the user grants access.
/// </summary>
internal sealed partial class WindowsNotificationService : INotificationService
{
    // Fallback when Windows does not deliver change events to desktop apps.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Backup scan while change events are subscribed (they can silently stop arriving).
    private static readonly TimeSpan BackupPollInterval = TimeSpan.FromSeconds(3);

    private readonly HashSet<uint> _seen = [];
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private UserNotificationListener? _listener;
    private Timer? _pollTimer;
    private bool _eventsSubscribed;
    private Teams.TeamsPopupWatcher? _teams;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Unknown;

    public event EventHandler<IslandNotification>? Received;

    public async Task<NotificationAccess> StartAsync()
    {
        // Teams' own pop-ups don't need notification access or package identity.
        // Started first, synchronously, because its window hook needs the UI thread.
        StartTeamsWatcher();

        if (!HasPackageIdentity())
        {
            DebugLog.Write("Notifications: no package identity");
            return Access = NotificationAccess.NeedsSetup;
        }

        if (_listener is not null && Access == NotificationAccess.Allowed)
            return Access;

        try
        {
            _listener = UserNotificationListener.Current;

            // Shows the Windows consent prompt the first time; must run on the UI thread.
            var status = await _listener.RequestAccessAsync();
            DebugLog.Write($"Notifications: access {status}");
            if (status != UserNotificationListenerAccessStatus.Allowed)
                return Access = NotificationAccess.Denied;

            // Existing notifications are history; only new ones should reach the island.
            var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notification in existing)
                _seen.Add(notification.Id);
            DebugLog.Write($"Notifications: {existing.Count} already in the notification centre from: " +
                string.Join(", ", existing.Select(n => SafeAppName(n)).Distinct()));

            try
            {
                _listener.NotificationChanged += OnNotificationChanged;
                _eventsSubscribed = true;
                DebugLog.Write("Notifications: listening for change events");
            }
            catch (Exception ex)
            {
                // Some Windows builds only deliver this event to UWP background tasks.
                DebugLog.Write($"Notifications: change events unavailable ({ex.GetType().Name}: {ex.Message})");
            }

            // Safety net: desktop apps sometimes subscribe successfully but never receive the event,
            // so a light scan runs as well (every second without events, every few seconds with them).
            var interval = _eventsSubscribed ? BackupPollInterval : PollInterval;
            _pollTimer = new Timer(_ => _ = ScanAsync(), null, interval, interval);

            return Access = NotificationAccess.Allowed;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Notifications: start failed: {ex}");
            return Access = NotificationAccess.Denied;
        }
    }

    private void StartTeamsWatcher()
    {
        if (_teams is not null)
            return;

        _teams = new Teams.TeamsPopupWatcher();
        _teams.Received += (_, notification) => Received?.Invoke(this, notification);
        try
        {
            _teams.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Teams: watcher failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public Task DismissAsync(IslandNotification notification)
    {
        if (notification.FromAppWindow)
            return Task.CompletedTask; // Not in Windows' notification centre.

        try
        {
            _listener?.RemoveNotification(notification.Id);
        }
        catch (Exception)
        {
            // Already gone from the notification centre.
        }

        return Task.CompletedTask;
    }

    public void OpenApp(IslandNotification notification)
    {
        if (string.IsNullOrWhiteSpace(notification.AppId))
            return;

        try
        {
            // Works for both packaged apps (Teams, WhatsApp) and classic desktop apps with an AUMID.
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{notification.AppId}") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // The app may have been uninstalled.
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added)
            return;

        _ = PublishAsync(args.UserNotificationId);
    }

    private async Task PublishAsync(uint id)
    {
        try
        {
            var notification = _listener?.GetNotification(id);
            if (notification is null)
                return;

            // Events can arrive concurrently on WinRT threads.
            lock (_seen)
            {
                if (!_seen.Add(id))
                    return;
            }

            DebugLog.Write($"Notifications: event from {SafeAppName(notification)}");
            await PublishConvertedAsync(notification);
        }
        catch (Exception ex)
        {
            // The notification may have been removed before it could be read.
            DebugLog.Write($"Notifications: event handling failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ScanAsync()
    {
        if (_listener is null || !await _scanGate.WaitAsync(0))
            return;

        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);

            List<UserNotification> fresh;
            lock (_seen)
            {
                fresh = current.Where(n => !_seen.Contains(n.Id)).OrderBy(n => n.CreationTime).ToList();

                // Forget removed notifications so the set does not grow forever.
                _seen.IntersectWith(current.Select(n => n.Id));
                foreach (var notification in fresh)
                    _seen.Add(notification.Id);
            }

            foreach (var notification in fresh)
            {
                DebugLog.Write($"Notifications: scan found one from {SafeAppName(notification)}");
                await PublishConvertedAsync(notification);
            }
        }
        catch (Exception ex)
        {
            // Access can be revoked while running; the next scan tries again.
            DebugLog.Write($"Notifications: scan failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task PublishConvertedAsync(UserNotification notification)
    {
        if (await ConvertAsync(notification) is { } converted)
            Received?.Invoke(this, converted);
        else
            DebugLog.Write($"Notifications: skipped one from {SafeAppName(notification)} (no readable text)");
    }

    /// <summary>App name for the debug log only; message contents are never logged.</summary>
    private static string SafeAppName(UserNotification notification)
    {
        try
        {
            return notification.AppInfo?.DisplayInfo?.DisplayName ?? "(unknown app)";
        }
        catch (Exception)
        {
            return "(unreadable app)";
        }
    }

    private static async Task<IslandNotification?> ConvertAsync(UserNotification notification)
    {
        var appInfo = notification.AppInfo;
        var appName = appInfo?.DisplayInfo?.DisplayName ?? "";
        var appId = appInfo?.AppUserModelId ?? "";

        // Never echo our own notifications.
        if (appName == "Dynamic Island")
            return null;

        var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements()
            .Select(t => t.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .ToList() ?? [];

        if (texts.Count == 0 && string.IsNullOrEmpty(appName))
            return null;

        return new IslandNotification(
            notification.Id,
            appId,
            string.IsNullOrEmpty(appName) ? "Notification" : appName,
            texts.FirstOrDefault() ?? appName,
            string.Join(Environment.NewLine, texts.Skip(1)),
            await ReadIconAsync(appInfo),
            notification.CreationTime);
    }

    private static async Task<byte[]?> ReadIconAsync(global::Windows.ApplicationModel.AppInfo? appInfo)
    {
        try
        {
            var logo = appInfo?.DisplayInfo?.GetLogo(new global::Windows.Foundation.Size(64, 64));
            if (logo is null)
                return null;

            using var winrtStream = await logo.OpenReadAsync();
            await using var stream = winrtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasPackageIdentity()
    {
        var length = 0u;
        // APPMODEL_ERROR_NO_PACKAGE (15700) means the process has no identity.
        return GetCurrentPackageFullName(ref length, 0) != 15700;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);

    public void Dispose()
    {
        _teams?.Dispose();
        _pollTimer?.Dispose();

        if (_eventsSubscribed && _listener is not null)
        {
            try
            {
                _listener.NotificationChanged -= OnNotificationChanged;
            }
            catch (Exception)
            {
                // Ignore: shutting down.
            }
        }

        _scanGate.Dispose();
    }
}
