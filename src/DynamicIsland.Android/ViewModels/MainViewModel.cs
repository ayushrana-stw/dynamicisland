using Android.Content;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Droid.Platform;
using DynamicIsland.UI.ViewModels;

namespace DynamicIsland.Droid.ViewModels;

/// <summary>The app's screen: the special permissions the island needs, then the shared settings.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Context _context = IslandRuntime.AppContext;

    public MainViewModel()
    {
        Settings = new SettingsViewModel(IslandRuntime.Settings, IslandRuntime.Startup, IslandRuntime.Notifications,
            hotkeyDescription: null, isMobile: true);
        Refresh();
    }

    public SettingsViewModel Settings { get; }

    /// <summary>The visible activity; system permission prompts need one.</summary>
    public global::Android.App.Activity? Activity { get; set; }

    [ObservableProperty] public partial bool HasAccessibilityOverlay { get; private set; }
    [ObservableProperty] public partial bool CanDrawOverlays { get; private set; }
    [ObservableProperty] public partial bool HasNotificationAccess { get; private set; }
    [ObservableProperty] public partial bool CanPostNotifications { get; private set; }
    [ObservableProperty] public partial bool IgnoresBatteryOptimizations { get; private set; }

    /// <summary>The notification prompt only exists on Android 13 and later.</summary>
    public bool AsksForNotifications => OperatingSystem.IsAndroidVersionAtLeast(33);

    /// <summary>Re-reads every permission; called whenever the user comes back to the app.</summary>
    public void Refresh()
    {
        HasAccessibilityOverlay = AndroidPermissions.HasAccessibilityOverlay;
        CanDrawOverlays = AndroidPermissions.CanDrawOverlays(_context);
        HasNotificationAccess = AndroidPermissions.HasNotificationAccess(_context);
        CanPostNotifications = AndroidPermissions.CanPostNotifications(_context);
        IgnoresBatteryOptimizations = AndroidPermissions.IgnoresBatteryOptimizations(_context);
        Settings.RefreshNotificationAccess();

        IslandRuntime.SyncIsland(_context);
    }

    [RelayCommand]
    private void AllowAccessibilityOverlay() => AndroidPermissions.OpenAccessibilitySettings(Activity ?? _context);

    [RelayCommand]
    private void AllowOverlay() => AndroidPermissions.OpenOverlaySettings(Activity ?? _context);

    [RelayCommand]
    private void AllowNotificationAccess() => AndroidPermissions.OpenNotificationAccessSettings(Activity ?? _context);

    [RelayCommand]
    private void AllowPostNotifications()
    {
        if (Activity is { } activity)
            AndroidPermissions.RequestPostNotifications(activity);
    }

    [RelayCommand]
    private void AllowBackground() => AndroidPermissions.RequestIgnoreBatteryOptimizations(Activity ?? _context);
}
