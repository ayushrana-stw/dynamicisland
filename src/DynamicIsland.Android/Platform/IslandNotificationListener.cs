using Android.App;
using Android.Content;
using Android.Service.Notification;

namespace DynamicIsland.Droid.Platform;

/// <summary>
/// Android binds this once the user turns on notification access. It also unlocks other apps' media
/// sessions (see <see cref="AndroidMediaService"/>). Android creates it, so it only relays events.
/// </summary>
[Service(
    Name = "com.dynamicisland.app.IslandNotificationListener",
    Label = "Dynamic Island",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class IslandNotificationListener : NotificationListenerService
{
    /// <summary>The connected listener, or null while access is off.</summary>
    public static IslandNotificationListener? Instance { get; private set; }

    /// <summary>Raised on the main thread for every new or updated notification.</summary>
    public static event EventHandler<StatusBarNotification>? Posted;

    /// <summary>Raised when the system connects or disconnects the listener (access turned on or off).</summary>
    public static event EventHandler? ConnectionChanged;

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        Instance = this;
        ConnectionChanged?.Invoke(null, EventArgs.Empty);
    }

    public override void OnListenerDisconnected()
    {
        if (Instance == this)
            Instance = null;

        base.OnListenerDisconnected();
        ConnectionChanged?.Invoke(null, EventArgs.Empty);
    }

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        if (sbn is not null)
            Posted?.Invoke(this, sbn);
    }

    /// <summary>Importance the user gave this notification's channel (silent ones are skipped).</summary>
    public NotificationImportance ImportanceOf(string key)
    {
        var ranking = new Ranking();
        return CurrentRanking?.GetRanking(key, ranking) == true && ranking.Channel is { } channel
            ? channel.Importance
            : NotificationImportance.Default;
    }
}
