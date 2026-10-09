namespace DynamicIsland.Core.Settings;

/// <summary>User preferences. Treat as immutable: change with a <c>with</c> expression and save through <see cref="SettingsService"/>.</summary>
/// <remarks>
/// Properties use <c>set</c>, not <c>init</c>: the JSON source generator assigns every <c>init</c> property,
/// so options missing from an older settings file would become false instead of keeping their defaults.
/// </remarks>
public sealed record IslandSettings
{
    // General
    public bool ShowIsland { get; set; } = true;
    public bool StartWithSystem { get; set; }

    /// <summary>-1 means "primary display"; otherwise an index into the current screen list.</summary>
    public int DisplayIndex { get; set; } = -1;

    /// <summary>Distance from the top edge of the display, in device-independent pixels.</summary>
    public double VerticalOffset { get; set; } = 8;

    /// <summary>Seconds without interaction before an expanded island collapses.</summary>
    public double AutoCollapseSeconds { get; set; } = 4;

    public bool HideInFullScreen { get; set; } = true;

    /// <summary>Hovering the compact island widens it to show the title or time.</summary>
    public bool HoverPreview { get; set; } = true;

    // System
    public bool ShowVolume { get; set; } = true;
    public bool ShowBrightness { get; set; } = true;
    public bool ShowBatteryAlerts { get; set; } = true;
    public bool ShowPrivacyIndicator { get; set; } = true;
    public bool TimerSound { get; set; } = true;

    // Media
    public bool ShowMedia { get; set; } = true;
    public bool ExpandOnTrackChange { get; set; } = true;
    public bool AnimatedVisualizer { get; set; } = true;
    public bool TintFromArtwork { get; set; } = true;

    // Notifications
    public bool ShowNotifications { get; set; } = true;
    public bool HideNotificationPreviews { get; set; }
    public bool ExpandOnNotification { get; set; } = true;

    /// <summary>Read new Teams' own pop-ups (it doesn't use Windows notifications).</summary>
    public bool ReadTeamsPopups { get; set; } = true;

    /// <summary>Display names of apps whose notifications are never shown.</summary>
    public string[] MutedNotificationApps { get; set; } = [];

    public bool IsMuted(string appName) =>
        MutedNotificationApps.Contains(appName, StringComparer.OrdinalIgnoreCase);

    public static IslandSettings Default { get; } = new();
}
