using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamicIsland.App.Platform;
using DynamicIsland.App.ViewModels;
using DynamicIsland.App.Views;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Platform;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private SettingsService _settings = null!;
    private IMediaService _media = null!;
    private INotificationService _notifications = null!;
    private ISystemStatusService _system = null!;
    private IStartupManager _startup = null!;
    private IslandViewModel _islandViewModel = null!;
    private IslandWindow _island = null!;
    private SettingsWindow? _settingsWindow;
    private NativeMenuItem _showIslandItem = null!;
    private TrayIcon? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Opens Settings whenever another launch of the app sets <paramref name="signal"/>
    /// (so double-clicking the exe again is a way back into Settings).
    /// </summary>
    internal static void WatchForOpenSettingsSignal(EventWaitHandle signal)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                    Dispatcher.UIThread.Post(() => (Current as App)?.ShowSettings());
            }
            catch (ObjectDisposedException)
            {
                // App is shutting down.
            }
        })
        { IsBackground = true, Name = "Open-settings signal" };
        thread.Start();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            _settings = new SettingsService();
            _settings.Load();
            _settings.Changed += (_, _) => UpdateIslandVisibility();

            var args = desktop.Args ?? [];
            var demo = args.Contains("--demo") || args.Contains("--snapshot");
            _media = demo ? new Dev.DemoMediaService() : PlatformServices.CreateMediaService();
            _notifications = demo ? new NullNotificationService() : PlatformServices.CreateNotificationService();
            _startup = PlatformServices.CreateStartupManager();

            _system = demo ? new NullSystemStatusService() : PlatformServices.CreateSystemStatusService();
            _islandViewModel = new IslandViewModel(_settings, _media, _notifications, _system);
            _islandViewModel.SettingsRequested += (_, _) => ShowSettings();
            _islandViewModel.ExitRequested += (_, _) => _desktop?.Shutdown();

            _island = new IslandWindow(PlatformServices.CreateIslandWindowPlatform()) { DataContext = _islandViewModel };
            _island.VisibilityPolicyChanged += (_, _) => UpdateIslandVisibility();

            CreateTrayIcon();
            UpdateIslandVisibility();
            _ = StartMediaAsync();
            _ = StartNotificationsAsync();
            _system.Start();

            desktop.Exit += (_, _) => Shutdown();

            // Demo: a sample notification a few seconds after start.
            if (args.Contains("--demo"))
                DispatcherTimer.RunOnce(() => _islandViewModel.ShowNotification(Dev.DemoNotifications.Teams()), TimeSpan.FromSeconds(4));

            if (args.Contains("--settings"))
                Dispatcher.UIThread.Post(ShowSettings);

            var snapshotIndex = Array.IndexOf(args, "--snapshot");
            if (snapshotIndex >= 0 && snapshotIndex + 1 < args.Length)
                _ = SnapshotAndExitAsync(args[snapshotIndex + 1]);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartMediaAsync()
    {
        try
        {
            await _media.StartAsync();
        }
        catch (Exception)
        {
            // Media integration is optional; the island still works without it.
        }
    }

    private async Task StartNotificationsAsync()
    {
        if (!_settings.Current.ShowNotifications)
            return;

        try
        {
            await _notifications.StartAsync();
        }
        catch (Exception)
        {
            // Notification access is optional; Settings shows its status.
        }
    }

    private async Task SnapshotAndExitAsync(string folder)
    {
        try
        {
            await Dev.Snapshotter.RunAsync(_island, _islandViewModel, _settings, folder);
        }
        finally
        {
            _desktop?.Shutdown();
        }
    }

    // ---- Island visibility -----------------------------------------------------------------

    private bool IslandShouldBeVisible => _settings.Current.ShowIsland && !_island.SuppressedByFullScreen;

    private void UpdateIslandVisibility()
    {
        if (IslandShouldBeVisible)
        {
            if (!_island.IsVisible)
            {
                _island.Show();
                _island.Reposition();
            }
        }
        else if (_island.IsVisible)
        {
            _islandViewModel.Collapse();
            _island.Hide();
        }

        _showIslandItem.IsChecked = _settings.Current.ShowIsland;
    }

    private void SetIslandEnabled(bool enabled) =>
        _settings.Update(_settings.Current with { ShowIsland = enabled });

    // ---- Settings window -------------------------------------------------------------------

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            var viewModel = new SettingsViewModel(_settings, _startup, _notifications, _island.HotkeyDescription);
            viewModel.SetDisplays(DescribeDisplays());

            _settingsWindow = new SettingsWindow { DataContext = viewModel, Icon = _trayIcon?.Icon };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    private IReadOnlyList<string> DescribeDisplays()
    {
        var screens = _island.Screens.All;
        return screens
            .Select((s, i) => $"Display {i + 1}  ·  {s.Bounds.Width}×{s.Bounds.Height}{(s.IsPrimary ? "  (primary)" : "")}")
            .ToList();
    }

    // ---- Tray / menu bar -------------------------------------------------------------------

    private void CreateTrayIcon()
    {
        _showIslandItem = new NativeMenuItem("Show island")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.Current.ShowIsland,
        };
        _showIslandItem.Click += (_, _) => SetIslandEnabled(!_settings.Current.ShowIsland);

        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) => ShowSettings();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => _desktop?.Shutdown();

        _trayIcon = new TrayIcon
        {
            Icon = CreateAppIcon(),
            ToolTipText = "Dynamic Island",
            Menu = new NativeMenu { _showIslandItem, settingsItem, new NativeMenuItemSeparator(), exitItem },
        };
        _trayIcon.Clicked += (_, _) => ShowSettings();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    /// <summary>Draws the app icon (a pill with an accent dot) so no binary assets are needed.</summary>
    private static WindowIcon CreateAppIcon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(
                Brushes.Black,
                new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 2.5),
                new RoundedRect(new Rect(3, 19, 58, 26), 13));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)), null, new Point(47, 32), 4.5, 4.5);
            context.DrawRectangle(Brushes.White, null, new RoundedRect(new Rect(13, 28, 18, 8), 4));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private void Shutdown()
    {
        if (_trayIcon is not null)
            _trayIcon.IsVisible = false;

        _islandViewModel.Dispose();
        _media.Dispose();
        _notifications.Dispose();
        _system.Dispose();
    }
}
