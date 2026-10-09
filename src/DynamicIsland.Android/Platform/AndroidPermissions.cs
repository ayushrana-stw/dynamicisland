using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidSettings = Android.Provider.Settings;
using Uri = Android.Net.Uri;

namespace DynamicIsland.Droid.Platform;

/// <summary>The special permissions the island needs, and the system screens that grant them.</summary>
internal static class AndroidPermissions
{
    /// <summary>"Display over other apps": required to show the island at all.</summary>
    public static bool CanDrawOverlays(Context context) => AndroidSettings.CanDrawOverlays(context);

    public static void OpenOverlaySettings(Context context) =>
        Launch(context, new Intent(AndroidSettings.ActionManageOverlayPermission, PackageUri(context)));

    /// <summary>"Notification access": notifications and the media sessions of other apps.</summary>
    public static bool HasNotificationAccess(Context context) =>
        NotificationManagerCompat.GetEnabledListenerPackages(context)?.Contains(context.PackageName!) == true;

    public static void OpenNotificationAccessSettings(Context context)
    {
        // Android 11+ can open the switch for this app directly; older versions show the list.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(IslandNotificationListener)));
            var intent = new Intent(AndroidSettings.ActionNotificationListenerDetailSettings)
                .PutExtra(AndroidSettings.ExtraNotificationListenerComponentName, component.FlattenToString());
            if (TryLaunch(context, intent))
                return;
        }

        Launch(context, new Intent(AndroidSettings.ActionNotificationListenerSettings));
    }

    /// <summary>"Accessibility": lets the island sit over the status bar, around the camera.</summary>
    public static bool HasAccessibilityOverlay => DynamicIsland.Droid.Overlay.IslandAccessibilityService.Instance is not null;

    public static void OpenAccessibilitySettings(Context context) =>
        Launch(context, new Intent(AndroidSettings.ActionAccessibilitySettings));

    /// <summary>Android 13+ asks before an app may post notifications (the "island is running" notice).</summary>
    public static bool CanPostNotifications(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(33)
        || ContextCompat.CheckSelfPermission(context, Manifest.Permission.PostNotifications) == Permission.Granted;

    public static void RequestPostNotifications(global::Android.App.Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            ActivityCompat.RequestPermissions(activity, [Manifest.Permission.PostNotifications], 1);
    }

    /// <summary>Without this, some phones stop the island after a while in the background.</summary>
    public static bool IgnoresBatteryOptimizations(Context context) =>
        context.GetSystemService(Context.PowerService) is PowerManager power
        && power.IsIgnoringBatteryOptimizations(context.PackageName);

    public static void RequestIgnoreBatteryOptimizations(Context context) =>
        Launch(context, new Intent(AndroidSettings.ActionRequestIgnoreBatteryOptimizations, PackageUri(context)));

    private static Uri PackageUri(Context context) => Uri.Parse("package:" + context.PackageName)!;

    private static void Launch(Context context, Intent intent) => TryLaunch(context, intent);

    private static bool TryLaunch(Context context, Intent intent)
    {
        if (context is not global::Android.App.Activity)
            intent.AddFlags(ActivityFlags.NewTask);

        try
        {
            context.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            // Some phones remove these screens; the user can still grant access from system settings.
            return false;
        }
    }
}
