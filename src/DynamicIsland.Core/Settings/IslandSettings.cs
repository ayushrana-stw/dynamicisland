namespace DynamicIsland.Core.Settings;

/// <summary>User preferences. Immutable; change with a <c>with</c> expression and save through <see cref="SettingsService"/>.</summary>
public sealed record IslandSettings
{
    // General
    public bool ShowIsland { get; init; } = true;
    public bool StartWithSystem { get; init; }

    /// <summary>-1 means "primary display"; otherwise an index into the current screen list.</summary>
    public int DisplayIndex { get; init; } = -1;

    /// <summary>Distance from the top edge of the display, in device-independent pixels.</summary>
    public double VerticalOffset { get; init; } = 8;

    /// <summary>Seconds without interaction before an expanded island collapses.</summary>
    public double AutoCollapseSeconds { get; init; } = 4;

    public bool HideInFullScreen { get; init; } = true;

    // Media
    public bool ShowMedia { get; init; } = true;
    public bool ExpandOnTrackChange { get; init; } = true;
    public bool AnimatedVisualizer { get; init; } = true;
    public bool TintFromArtwork { get; init; } = true;

    // Notifications
    public bool ShowNotifications { get; init; } = true;
    public bool HideNotificationPreviews { get; init; }
    public bool ExpandOnNotification { get; init; } = true;

    /// <summary>Display names of apps whose notifications are never shown.</summary>
    public string[] MutedNotificationApps { get; init; } = [];

    public bool IsMuted(string appName) =>
        MutedNotificationApps.Contains(appName, StringComparer.OrdinalIgnoreCase);

    public static IslandSettings Default { get; } = new();
}
