using Android.App;
using Android.Content;

namespace DynamicIsland.Droid.Platform;

/// <summary>Brings the island back after the phone restarts (if the user asked for that) or the app updates.</summary>
[BroadcastReceiver(Name = "com.dynamicisland.app.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
            return;

        var settings = IslandRuntime.Settings.Current;
        var wanted = intent?.Action == Intent.ActionBootCompleted ? settings.StartWithSystem : settings.ShowIsland;
        if (wanted)
            IslandRuntime.SyncIsland(context);
    }
}
