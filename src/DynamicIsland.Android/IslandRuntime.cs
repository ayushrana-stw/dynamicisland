using Android.Content;
using DynamicIsland.Core.Settings;
using DynamicIsland.Droid.Overlay;
using DynamicIsland.Droid.Platform;

namespace DynamicIsland.Droid;

/// <summary>
/// Services shared by everything in the app process: the settings screen, the overlay service and the
/// boot receiver all see the same settings, so a change on one screen reaches the island immediately.
/// </summary>
internal static class IslandRuntime
{
    private static readonly Lazy<SettingsService> LazySettings = new(() =>
    {
        var settings = new SettingsService();
        settings.Load();
        settings.Changed += (_, _) => SyncIsland(AppContext);
        return settings;
    });

    private static readonly Lazy<AndroidNotificationService> LazyNotifications = new(() => new AndroidNotificationService(AppContext));

    public static Context AppContext => global::Android.App.Application.Context;

    public static SettingsService Settings => LazySettings.Value;

    public static AndroidNotificationService Notifications => LazyNotifications.Value;

    public static AndroidStartupManager Startup { get; } = new(() => Settings);

    /// <summary>
    /// Shows or hides the island to match the "Show the island" setting, hosted by the accessibility service
    /// when the user turned it on (over the status bar), otherwise by the foreground service (below it).
    /// </summary>
    public static void SyncIsland(Context context)
    {
        var show = Settings.Current.ShowIsland;
        var accessibility = IslandAccessibilityService.Instance;
        accessibility?.SetIslandVisible(show);

        var useForegroundService = show && accessibility is null && AndroidPermissions.CanDrawOverlays(context);
        if (useForegroundService && !IslandOverlayService.IsRunning)
            IslandOverlayService.Start(context);
        else if (!useForegroundService && IslandOverlayService.IsRunning)
            IslandOverlayService.Stop(context);
    }
}
