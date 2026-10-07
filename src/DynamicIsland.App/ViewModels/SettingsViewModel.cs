using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Platform;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.ViewModels;

/// <summary>Two-way view of <see cref="IslandSettings"/>; every change is saved immediately.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IStartupManager _startup;
    private readonly INotificationService _notifications;
    private bool _loading;

    public SettingsViewModel(SettingsService settings, IStartupManager startup, INotificationService notifications, string? hotkeyDescription)
    {
        _settings = settings;
        _startup = startup;
        _notifications = notifications;
        HotkeyText = hotkeyDescription is null
            ? "Click the island to open it."
            : $"Press {hotkeyDescription} to open the island from anywhere.";

        _settings.Changed += (_, s) => MutedApps = s.MutedNotificationApps;
        Load(settings.Current);
    }

    public bool StartupSupported => _startup.IsSupported;
    public string HotkeyText { get; }

    // ---- Notification access -----------------------------------------------------------------

    public string NotificationStatus => _notifications.Access switch
    {
        NotificationAccess.Allowed => "On — notifications from Teams, Outlook, WhatsApp and other apps appear in the island.",
        NotificationAccess.Denied => "Windows is blocking access. Allow Dynamic Island under Settings › Privacy & security › Notifications, then try again.",
        NotificationAccess.NeedsSetup => "One-time setup needed: turn on Developer Mode, then run scripts\\register-package.ps1. See the README.",
        NotificationAccess.Unsupported => "Not available on this system yet.",
        _ => "Not turned on yet.",
    };

    public bool CanRequestNotificationAccess => _notifications.Access is NotificationAccess.Unknown or NotificationAccess.Denied;
    public bool NotificationsWorking => _notifications.Access == NotificationAccess.Allowed;

    [RelayCommand]
    private async Task RequestNotificationAccess()
    {
        await _notifications.StartAsync();
        OnPropertyChanged(nameof(NotificationStatus));
        OnPropertyChanged(nameof(CanRequestNotificationAccess));
        OnPropertyChanged(nameof(NotificationsWorking));
    }

    [ObservableProperty] public partial string[] MutedApps { get; private set; } = [];

    public bool HasMutedApps => MutedApps.Length > 0;

    partial void OnMutedAppsChanged(string[] value) => OnPropertyChanged(nameof(HasMutedApps));

    [RelayCommand]
    private void Unmute(string appName) =>
        _settings.Update(_settings.Current with
        {
            MutedNotificationApps = _settings.Current.MutedNotificationApps
                .Where(a => !a.Equals(appName, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        });

    /// <summary>"Primary display" followed by every connected display.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> Displays { get; set; } = ["Primary display"];

    // General
    [ObservableProperty] public partial bool ShowIsland { get; set; }
    [ObservableProperty] public partial bool StartWithSystem { get; set; }
    [ObservableProperty] public partial int SelectedDisplay { get; set; }
    [ObservableProperty] public partial double VerticalOffset { get; set; }
    [ObservableProperty] public partial double AutoCollapseSeconds { get; set; }
    [ObservableProperty] public partial bool HideInFullScreen { get; set; }

    // Media
    [ObservableProperty] public partial bool ShowMedia { get; set; }
    [ObservableProperty] public partial bool ExpandOnTrackChange { get; set; }
    [ObservableProperty] public partial bool AnimatedVisualizer { get; set; }
    [ObservableProperty] public partial bool TintFromArtwork { get; set; }

    // Notifications
    [ObservableProperty] public partial bool ShowNotifications { get; set; }
    [ObservableProperty] public partial bool HideNotificationPreviews { get; set; }
    [ObservableProperty] public partial bool ExpandOnNotification { get; set; }

    public string VerticalOffsetText => $"{VerticalOffset:0} px";
    public string AutoCollapseText => $"{AutoCollapseSeconds:0} s";

    public void SetDisplays(IReadOnlyList<string> displayNames)
    {
        _loading = true;
        Displays = ["Primary display", .. displayNames];
        SelectedDisplay = Math.Clamp(_settings.Current.DisplayIndex + 1, 0, Displays.Count - 1);
        _loading = false;
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _settings.RestoreDefaults();
        TrySetStartup(IslandSettings.Default.StartWithSystem);
        Load(_settings.Current);
    }

    private void Load(IslandSettings s)
    {
        _loading = true;
        ShowIsland = s.ShowIsland;
        StartWithSystem = _startup.IsSupported && _startup.IsEnabled;
        SelectedDisplay = Math.Clamp(s.DisplayIndex + 1, 0, Displays.Count - 1);
        VerticalOffset = s.VerticalOffset;
        AutoCollapseSeconds = s.AutoCollapseSeconds;
        HideInFullScreen = s.HideInFullScreen;
        ShowMedia = s.ShowMedia;
        ExpandOnTrackChange = s.ExpandOnTrackChange;
        AnimatedVisualizer = s.AnimatedVisualizer;
        TintFromArtwork = s.TintFromArtwork;
        ShowNotifications = s.ShowNotifications;
        HideNotificationPreviews = s.HideNotificationPreviews;
        ExpandOnNotification = s.ExpandOnNotification;
        MutedApps = s.MutedNotificationApps;
        _loading = false;
    }

    /// <summary>Properties that map to a saved setting; anything else is display-only.</summary>
    private static readonly HashSet<string> SettingProperties =
    [
        nameof(ShowIsland), nameof(StartWithSystem), nameof(SelectedDisplay), nameof(VerticalOffset),
        nameof(AutoCollapseSeconds), nameof(HideInFullScreen), nameof(ShowMedia), nameof(ExpandOnTrackChange),
        nameof(AnimatedVisualizer), nameof(TintFromArtwork), nameof(ShowNotifications),
        nameof(HideNotificationPreviews), nameof(ExpandOnNotification),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(VerticalOffset))
            OnPropertyChanged(nameof(VerticalOffsetText));
        if (e.PropertyName == nameof(AutoCollapseSeconds))
            OnPropertyChanged(nameof(AutoCollapseText));

        if (_loading || e.PropertyName is null || !SettingProperties.Contains(e.PropertyName))
            return;

        if (e.PropertyName == nameof(StartWithSystem))
            TrySetStartup(StartWithSystem);

        if (e.PropertyName == nameof(ShowNotifications) && ShowNotifications)
            _ = RequestNotificationAccess();

        // Start from the current settings so values edited elsewhere (muted apps) are kept.
        _settings.Update(_settings.Current with
        {
            ShowIsland = ShowIsland,
            StartWithSystem = StartWithSystem,
            DisplayIndex = SelectedDisplay - 1,
            VerticalOffset = Math.Round(VerticalOffset),
            AutoCollapseSeconds = Math.Round(AutoCollapseSeconds),
            HideInFullScreen = HideInFullScreen,
            ShowMedia = ShowMedia,
            ExpandOnTrackChange = ExpandOnTrackChange,
            AnimatedVisualizer = AnimatedVisualizer,
            TintFromArtwork = TintFromArtwork,
            ShowNotifications = ShowNotifications,
            HideNotificationPreviews = HideNotificationPreviews,
            ExpandOnNotification = ExpandOnNotification,
        });
    }

    private void TrySetStartup(bool enabled)
    {
        try
        {
            _startup.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Reflect the real state if the OS refused the change.
            _loading = true;
            StartWithSystem = _startup.IsEnabled;
            _loading = false;
        }
    }
}
