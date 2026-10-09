using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using DynamicIsland.Droid.Views;

namespace DynamicIsland.Droid;

/// <summary>Settings and permissions. The island itself keeps running after this screen closes.</summary>
[Activity(
    Label = "Dynamic Island",
    Theme = "@style/IslandTheme",
    Icon = "@drawable/ic_launcher",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnResume()
    {
        base.OnResume();

        // The user may be coming back from a system permission screen.
        if (Content is MainView view)
        {
            view.ViewModel.Activity = this;
            view.ViewModel.Refresh();
        }
    }

    protected override void OnPause()
    {
        if (Content is MainView view)
            view.ViewModel.Activity = null;

        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        (Content as MainView)?.ViewModel.Refresh();
    }
}
