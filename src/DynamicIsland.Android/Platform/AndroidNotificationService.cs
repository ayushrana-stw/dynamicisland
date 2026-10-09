using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.Notification;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Droid.Platform;

/// <summary>
/// Notifications from every app, read through <see cref="IslandNotificationListener"/>.
/// Ongoing, silent and progress notifications are skipped, as are repeats of one already shown.
/// </summary>
internal sealed class AndroidNotificationService : INotificationService
{
    // Media controls, downloads, navigation and the like: status, not news. (Literal names so older Android versions match too.)
    private static readonly HashSet<string> SkippedCategories =
        ["transport", "progress", "service", "status", "sys", "navigation", "stopwatch"];

    private readonly Context _context;
    private readonly object _gate = new();
    private readonly Dictionary<uint, Sent> _sent = [];
    private readonly Dictionary<string, string> _lastContentByKey = [];
    private uint _nextId;

    private sealed record Sent(string Key, string PackageName, PendingIntent? ContentIntent);

    public AndroidNotificationService(Context context)
    {
        _context = context;
        IslandNotificationListener.Posted += OnPosted;
    }

    public NotificationAccess Access =>
        AndroidPermissions.HasNotificationAccess(_context) ? NotificationAccess.Allowed : NotificationAccess.Unknown;

    public event EventHandler<IslandNotification>? Received;

    /// <summary>Opens the system "Notification access" screen if access is still off.</summary>
    public Task<NotificationAccess> StartAsync()
    {
        if (Access != NotificationAccess.Allowed)
            AndroidPermissions.OpenNotificationAccessSettings(_context);

        return Task.FromResult(Access);
    }

    public Task DismissAsync(IslandNotification notification)
    {
        if (Take(notification.Id) is { } sent)
            IslandNotificationListener.Instance?.CancelNotification(sent.Key);

        return Task.CompletedTask;
    }

    public void OpenApp(IslandNotification notification)
    {
        var sent = Find(notification.Id);
        if (sent?.ContentIntent is { } intent && TrySend(intent))
            return;

        // No tap action (or it was cancelled): just bring the app to the front.
        var launch = _context.PackageManager?.GetLaunchIntentForPackage(sent?.PackageName ?? notification.AppId);
        if (launch is null)
            return;

        launch.AddFlags(ActivityFlags.NewTask);
        _context.StartActivity(launch);
    }

    private static bool TrySend(PendingIntent intent)
    {
        try
        {
            // Android 14+ only lets a background sender start the activity if it opts in;
            // the overlay permission is what allows the island to do so.
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                var options = ActivityOptions.MakeBasic()!;
                options.SetPendingIntentBackgroundActivityStartMode(BackgroundActivityStartMode.Allowed);
                intent.Send(null, 0, null, null, null, null, options.ToBundle());
            }
            else
            {
                intent.Send();
            }

            return true;
        }
        catch (PendingIntent.CanceledException)
        {
            return false;
        }
    }

    private void OnPosted(object? sender, StatusBarNotification sbn)
    {
        try
        {
            if (Convert(sender as IslandNotificationListener, sbn) is { } notification)
                Received?.Invoke(this, notification);
        }
        catch (Exception ex)
        {
            // One odd notification must never take the island down.
            DebugLog.Write($"Android notifications: skipped {sbn.PackageName}: {ex.Message}");
        }
    }

    private IslandNotification? Convert(IslandNotificationListener? listener, StatusBarNotification sbn)
    {
        var n = sbn.Notification;
        var key = sbn.Key;
        if (n is null || key is null || sbn.PackageName is not { } package || package == _context.PackageName)
            return null;

        if (sbn.IsOngoing || n.Flags.HasFlag(NotificationFlags.ForegroundService) || n.Flags.HasFlag(NotificationFlags.GroupSummary))
            return null;

        if (n.Category is { } category && SkippedCategories.Contains(category))
            return null;

        // Silent notifications don't pop up on the phone either.
        if (listener?.ImportanceOf(key) is { } importance && importance < NotificationImportance.Default)
            return null;

        var extras = n.Extras;
        var title = Text(extras, Notification.ExtraTitle) ?? Text(extras, Notification.ExtraConversationTitle) ?? "";
        var body = Text(extras, Notification.ExtraText) ?? Text(extras, Notification.ExtraBigText) ?? "";
        if (title.Length == 0 && body.Length == 0)
            return null;

        // Apps re-post a notification to update it (timestamps, progress); show it again only if the text changed.
        var content = title + "\u001f" + body;
        lock (_gate)
        {
            if (_lastContentByKey.TryGetValue(key, out var last)
                && (last == content || n.Flags.HasFlag(NotificationFlags.OnlyAlertOnce)))
                return null;

            _lastContentByKey[key] = content;
            if (_lastContentByKey.Count > 200)
                _lastContentByKey.Remove(_lastContentByKey.Keys.First());
        }

        var appName = AppInfo.Label(_context, package);
        var id = Remember(new Sent(key, package, n.ContentIntent));

        return new IslandNotification(
            id,
            package,
            appName,
            title.Length > 0 ? title : appName,
            body,
            AppInfo.IconPng(_context, package),
            DateTimeOffset.FromUnixTimeMilliseconds(sbn.PostTime).ToLocalTime());
    }

    private static string? Text(Bundle? extras, string? name) =>
        name is null ? null : extras?.GetCharSequence(name)?.ToString()?.Trim() is { Length: > 0 } text ? text : null;

    private uint Remember(Sent sent)
    {
        lock (_gate)
        {
            var id = ++_nextId;
            _sent[id] = sent;

            // The island keeps at most a few dozen notifications in its history.
            _sent.Remove(id - 100);
            return id;
        }
    }

    private Sent? Find(uint id)
    {
        lock (_gate)
            return _sent.GetValueOrDefault(id);
    }

    private Sent? Take(uint id)
    {
        lock (_gate)
            return _sent.Remove(id, out var sent) ? sent : null;
    }

    public void Dispose() => IslandNotificationListener.Posted -= OnPosted;
}
