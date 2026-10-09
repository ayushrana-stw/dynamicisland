using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using Android.Views;
using AndroidX.Core.Content;

namespace DynamicIsland.Droid.Overlay;

/// <summary>
/// Keeps the island on screen when the accessibility service is off. Android requires a foreground service
/// (with its small "running" notification) for that; the manifest declares it as a special use.
/// Its overlay can't sit over the status bar, so the island appears just below it.
/// </summary>
[Register("com.dynamicisland.app.IslandOverlayService")]
public sealed class IslandOverlayService : Service
{
    private const int RunningNotificationId = 1;
    private const string ChannelId = "island_running";
    private const string ActionHide = "com.dynamicisland.app.HIDE_ISLAND";

    private IslandHost? _island;

    public static bool IsRunning { get; private set; }

    public static void Start(Context context) =>
        ContextCompat.StartForegroundService(context, new Intent(context, typeof(IslandOverlayService)));

    public static void Stop(Context context) =>
        context.StopService(new Intent(context, typeof(IslandOverlayService)));

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        IsRunning = true;
        StartInForeground();
        _island = new IslandHost(this, WindowManagerTypes.ApplicationOverlay);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // "Hide" on the running notification turns the island off until the user turns it back on.
        if (intent?.Action == ActionHide)
            IslandRuntime.Settings.Update(IslandRuntime.Settings.Current with { ShowIsland = false });

        return StartCommandResult.Sticky;
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _island?.Reposition();
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        _island?.Dispose();
        base.OnDestroy();
    }

    private void StartInForeground()
    {
        // Lowest importance: no sound, no status-bar icon; it sits quietly at the bottom of the shade.
        var channel = new NotificationChannel(ChannelId, "Island running", NotificationImportance.Min)
        {
            Description = "Shown while the island is on screen. Android requires it for apps that stay on top.",
        };
        channel.SetShowBadge(false);
        ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(channel);

        var immutable = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;
        var openSettings = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), immutable);
        var hide = PendingIntent.GetService(this, 1, new Intent(this, typeof(IslandOverlayService)).SetAction(ActionHide), immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_island);
        builder.SetContentTitle("Dynamic Island is on");
        builder.SetContentText("Tap for settings");
        builder.SetContentIntent(openSettings);
        builder.AddAction(0, "Hide island", hide);
        builder.SetOngoing(true);
        builder.SetPriority(NotificationCompat.PriorityMin);
        builder.SetCategory(NotificationCompat.CategoryService);
        var notification = builder.Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(RunningNotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(RunningNotificationId, notification);
    }
}
