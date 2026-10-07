using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.ViewModels;

/// <summary>What the island is showing right now. Exactly one mode is active.</summary>
public enum IslandMode
{
    CompactIdle,
    CompactMedia,
    CompactTimer,
    Hud,
    ExpandedIdle,
    ExpandedMedia,
    ExpandedNotification,
    ExpandedHistory,
    ExpandedTimerPicker,
    ExpandedTimer,
}

/// <summary>A page the user navigated to inside the expanded island.</summary>
internal enum ExpandedPage
{
    Auto,
    History,
    TimerPicker,
    Timer,
}

/// <summary>
/// The island's state. Split into partial files by feature:
/// Media, Notifications (+ history), System (volume/brightness/battery/privacy) and Timer.
/// </summary>
public sealed partial class IslandViewModel : ObservableObject, IDisposable
{
    // The window is sized to fit the largest island plus the side bubble.
    public const double MaxWidth = 440;
    public const double MaxHeight = 300;

    private const double BubbleSize = 34;
    private const double BubbleGap = 8;

    private static readonly string[] ModeProperties =
    [
        nameof(ShowCompactIdle), nameof(ShowCompactMedia), nameof(ShowCompactTimer), nameof(ShowHud),
        nameof(ShowExpandedIdle), nameof(ShowExpandedMedia), nameof(ShowExpandedNotification),
        nameof(ShowExpandedHistory), nameof(ShowExpandedTimerPicker), nameof(ShowExpandedTimer),
        nameof(IsCompact), nameof(ShowBubble), nameof(BubbleShowsTimer), nameof(BubbleShowsPrivacy),
        nameof(ShowPrivacyDot), nameof(ShowHoverTitle), nameof(ShowHoverClock),
    ];

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _hoverTimer;

    private ExpandedPage _page;
    private bool _pointerInside;
    private int _actionsInProgress;

    public IslandViewModel(SettingsService settings, IMediaService media, INotificationService notifications, ISystemStatusService system)
    {
        _settings = settings;
        _media = media;
        _notifications = notifications;
        _system = system;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.Current.AutoCollapseSeconds) };
        _collapseTimer.Tick += (_, _) => TryAutoCollapse();

        // Drives the clock, progress bar and timer; runs only while one of them is on screen.
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick();

        // A short delay so the island does not twitch when the mouse just passes over it.
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            IsHovering = !IsExpanded && Settings.HoverPreview;
        };

        _hudTimer = new DispatcherTimer();
        _hudTimer.Tick += (_, _) => HideHud();

        _settings.Changed += OnSettingsChanged;
        _media.Changed += OnMediaChanged;
        _notifications.Received += OnNotificationReceived;
        _system.VolumeChanged += OnVolumeChanged;
        _system.BrightnessChanged += OnBrightnessChanged;
        _system.PowerChanged += OnPowerChanged;
        _system.PrivacyChanged += OnPrivacyChanged;

        ApplyAccent();
        ApplyMedia(_media.Current);
        ApplyPrivacy();
        UpdateShape();
    }

    /// <summary>Raised when the user asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when a different song starts, so the view can play a "bump" animation.</summary>
    public event EventHandler? TrackChanged;

    /// <summary>Raised when a notification is shown, so the view can bump the app icon.</summary>
    public event EventHandler? NotificationArrived;

    public IslandSettings Settings => _settings.Current;

    // ---- Shape and mode ----------------------------------------------------------------------

    [ObservableProperty] public partial bool IsExpanded { get; private set; }
    [ObservableProperty] public partial bool IsHovering { get; private set; }
    [ObservableProperty] public partial bool HasUnseenActivity { get; private set; }
    [ObservableProperty] public partial IslandMode Mode { get; private set; }

    [ObservableProperty] public partial double IslandWidth { get; private set; }
    [ObservableProperty] public partial double IslandHeight { get; private set; }
    [ObservableProperty] public partial CornerRadius IslandCornerRadius { get; private set; }

    /// <summary>Places the side bubble just right of the island (the island is centred).</summary>
    [ObservableProperty] public partial Thickness BubbleMargin { get; private set; }

    public bool ShowCompactIdle => Mode == IslandMode.CompactIdle;
    public bool ShowCompactMedia => Mode == IslandMode.CompactMedia;
    public bool ShowCompactTimer => Mode == IslandMode.CompactTimer;
    public bool ShowHud => Mode == IslandMode.Hud;
    public bool ShowExpandedIdle => Mode == IslandMode.ExpandedIdle;
    public bool ShowExpandedMedia => Mode == IslandMode.ExpandedMedia;
    public bool ShowExpandedNotification => Mode == IslandMode.ExpandedNotification;
    public bool ShowExpandedHistory => Mode == IslandMode.ExpandedHistory;
    public bool ShowExpandedTimerPicker => Mode == IslandMode.ExpandedTimerPicker;
    public bool ShowExpandedTimer => Mode == IslandMode.ExpandedTimer;
    public bool IsCompact => Mode is IslandMode.CompactIdle or IslandMode.CompactMedia or IslandMode.CompactTimer;

    public bool ShowHoverTitle => ShowCompactMedia && IsHovering;
    public bool ShowHoverClock => ShowCompactIdle && IsHovering;

    // The split island: a separate bubble for a second activity while the island is compact.
    public bool BubbleShowsTimer => ShowCompactMedia && TimerActive;
    public bool BubbleShowsPrivacy => IsCompact && !BubbleShowsTimer && ShowPrivacy;
    public bool ShowBubble => BubbleShowsTimer || BubbleShowsPrivacy;

    /// <summary>Small privacy dot inside the pill when the bubble is busy with something else.</summary>
    public bool ShowPrivacyDot => IsCompact && ShowPrivacy && !BubbleShowsPrivacy;

    /// <summary>Width the window must keep interactive to the right of the island for the bubble.</summary>
    public double BubbleExtent => ShowBubble ? BubbleGap + BubbleSize : 0;

    private IslandMode ComputeMode()
    {
        if (!IsExpanded)
        {
            if (HudVisible)
                return IslandMode.Hud;
            if (HasMedia)
                return IslandMode.CompactMedia;
            return TimerActive ? IslandMode.CompactTimer : IslandMode.CompactIdle;
        }

        if (HasNotification)
            return IslandMode.ExpandedNotification;

        return _page switch
        {
            ExpandedPage.History => IslandMode.ExpandedHistory,
            ExpandedPage.TimerPicker => IslandMode.ExpandedTimerPicker,
            ExpandedPage.Timer when TimerActive => IslandMode.ExpandedTimer,
            _ when TimerFinished => IslandMode.ExpandedTimer,
            _ when HasMedia => IslandMode.ExpandedMedia,
            _ when TimerActive => IslandMode.ExpandedTimer,
            _ => IslandMode.ExpandedIdle,
        };
    }

    private Size SizeFor(IslandMode mode) => mode switch
    {
        IslandMode.CompactIdle => IsHovering ? new Size(196, 34) : new Size(128, 34),
        IslandMode.CompactMedia => IsHovering ? new Size(340, 34) : new Size(236, 34),
        IslandMode.CompactTimer => new Size(206, 34),
        IslandMode.Hud => new Size(300, 34),
        IslandMode.ExpandedIdle => new Size(340, 118),
        IslandMode.ExpandedMedia => HasTimeline ? new Size(372, 190) : new Size(372, 156),
        IslandMode.ExpandedNotification => new Size(372, 116),
        IslandMode.ExpandedHistory => new Size(372, HistoryHeight),
        IslandMode.ExpandedTimerPicker => new Size(372, 160),
        IslandMode.ExpandedTimer => new Size(372, 132),
        _ => new Size(128, 34),
    };

    private void UpdateShape()
    {
        Mode = ComputeMode();
        var size = SizeFor(Mode);

        IslandWidth = size.Width;
        IslandHeight = size.Height;
        IslandCornerRadius = new CornerRadius(IsCompact || Mode == IslandMode.Hud ? size.Height / 2 : 38);
        BubbleMargin = new Thickness(size.Width + 2 * BubbleGap + BubbleSize, 4, 0, 0);

        foreach (var property in ModeProperties)
            OnPropertyChanged(property);
        OnPropertyChanged(nameof(BubbleExtent));

        NotifyAnimations();
        UpdateTicker();
    }

    // ---- Expand / collapse ---------------------------------------------------------------------

    [RelayCommand]
    public void ToggleExpanded()
    {
        if (IsExpanded)
            Collapse();
        else
            Expand(peek: false);
    }

    [RelayCommand]
    public void Collapse()
    {
        _collapseTimer.Stop();
        _page = ExpandedPage.Auto;
        IsExpanded = false;

        // Seen notifications leave the island (history and Windows' notification centre keep them).
        _notificationQueue.Clear();
        HasNotification = false;

        if (TimerFinished)
            StopTimer();

        UpdateShape();
    }

    public void Expand(bool peek)
    {
        HasUnseenActivity = false;
        IsHovering = false;
        IsExpanded = true;

        // A peek (triggered by an event, not the user) closes on its own unless the user engages.
        if (peek && !_pointerInside)
            RestartCollapseTimer();
    }

    /// <summary>Shows a page inside the expanded island, expanding it if needed.</summary>
    private void NavigateTo(ExpandedPage page)
    {
        _page = page;
        _notificationQueue.Clear();
        HasNotification = false;

        if (!IsExpanded)
            Expand(peek: false);
        UpdateShape();
    }

    [RelayCommand]
    private void Back() => NavigateTo(ExpandedPage.Auto);

    public void OnPointerEntered()
    {
        _pointerInside = true;
        _collapseTimer.Stop();

        if (!IsExpanded && Settings.HoverPreview)
            _hoverTimer.Start();
    }

    public void OnPointerExited()
    {
        _pointerInside = false;
        _hoverTimer.Stop();
        IsHovering = false;

        if (IsExpanded)
            RestartCollapseTimer();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Collapse();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RestartCollapseTimer(double extraSeconds = 0)
    {
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Settings.AutoCollapseSeconds) + extraSeconds);
        _collapseTimer.Start();
    }

    private void TryAutoCollapse()
    {
        if (_pointerInside || _actionsInProgress > 0)
            return; // Keep waiting; the timer fires again.

        Collapse();
    }

    // ---- Ticking -------------------------------------------------------------------------------

    [ObservableProperty] public partial string ClockText { get; private set; } = "";
    [ObservableProperty] public partial string DateText { get; private set; } = "";

    /// <summary>The 1-second ticker runs only while something time-based is on screen.</summary>
    private void UpdateTicker()
    {
        var needed = IsExpanded || TimerActive || ShowHoverClock;
        if (needed && !_tickTimer.IsEnabled)
        {
            Tick();
            _tickTimer.Start();
        }
        else if (!needed)
        {
            _tickTimer.Stop();
        }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("t");
        DateText = now.ToString("dddd, MMMM d");

        if (ShowExpandedMedia && IsPlaying)
            UpdateProgress(smooth: true);

        TickTimer();
    }

    // ---- Settings ------------------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, IslandSettings settings)
    {
        OnPropertyChanged(nameof(Settings));
        ApplyAccent();
        ApplyMedia(_media.Current);
        ApplyPrivacy();
        UpdateShape();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            HideHud();
            UpdateProgress(smooth: false);
            RefreshHistoryTimes();
        }

        UpdateShape();
    }

    partial void OnIsHoveringChanged(bool value) => UpdateShape();

    private void NotifyAnimations()
    {
        OnPropertyChanged(nameof(AnimateCompactVisualizer));
        OnPropertyChanged(nameof(AnimateExpandedVisualizer));
        OnPropertyChanged(nameof(ShowAmbientGlow));
        OnPropertyChanged(nameof(AnimateMarquee));
        OnPropertyChanged(nameof(AnimateHoverMarquee));
    }

    public void Dispose()
    {
        _collapseTimer.Stop();
        _tickTimer.Stop();
        _hoverTimer.Stop();
        _hudTimer.Stop();
        _settings.Changed -= OnSettingsChanged;
        _media.Changed -= OnMediaChanged;
        _notifications.Received -= OnNotificationReceived;
        _system.VolumeChanged -= OnVolumeChanged;
        _system.BrightnessChanged -= OnBrightnessChanged;
        _system.PowerChanged -= OnPowerChanged;
        _system.PrivacyChanged -= OnPrivacyChanged;
        Artwork?.Dispose();
        NotificationIcon?.Dispose();
    }
}
