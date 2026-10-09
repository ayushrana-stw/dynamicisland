using Android.Content;
using Android.Views;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.SystemStatus;
using DynamicIsland.Droid.Platform;
using DynamicIsland.UI.ViewModels;

namespace DynamicIsland.Droid.Overlay;

/// <summary>
/// The live island: media, system status, the view model and the overlay window.
/// Created by whichever service hosts it (<see cref="IslandAccessibilityService"/> or <see cref="IslandOverlayService"/>).
/// </summary>
internal sealed class IslandHost : IDisposable
{
    private readonly Context _context;
    private readonly IMediaService _media;
    private readonly ISystemStatusService _system;
    private readonly IslandViewModel _viewModel;
    private readonly IslandOverlay _overlay;

    public IslandHost(Context context, WindowManagerTypes windowType)
    {
        _context = context;
        _media = new AndroidMediaService(context);
        _system = new AndroidSystemStatusService(context);
        _viewModel = new IslandViewModel(IslandRuntime.Settings, _media, IslandRuntime.Notifications, _system)
        {
            PlayAlert = () => AndroidSound.PlayAlert(context),
        };
        _viewModel.SettingsRequested += (_, _) => OpenSettings();

        // "Exit" on a phone turns the island off; it comes back from the app's "Show the island" switch.
        _viewModel.ExitRequested += (_, _) =>
            IslandRuntime.Settings.Update(IslandRuntime.Settings.Current with { ShowIsland = false });

        _overlay = new IslandOverlay(context, _viewModel, windowType);
        _overlay.LongPressed += (_, _) => OpenSettings();
        _overlay.Show();

        _ = _media.StartAsync();
        _system.Start();
    }

    /// <summary>Re-centres the island after the screen rotates.</summary>
    public void Reposition() => _overlay.Reposition();

    private void OpenSettings()
    {
        _viewModel.Collapse();
        var intent = new Intent(_context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront);
        _context.StartActivity(intent);
    }

    public void Dispose()
    {
        _overlay.Dispose();
        _viewModel.Dispose();
        _media.Dispose();
        _system.Dispose();
    }
}
