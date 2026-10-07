using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.ViewModels;

public sealed partial class IslandViewModel : ObservableObject, IDisposable
{
    // Island sizes in device-independent pixels. The window is sized to fit the largest one.
    public const double MaxWidth = 400;
    public const double MaxHeight = 186;

    /// <summary>Width of the progress track in the expanded media view.</summary>
    public const double ProgressTrackWidth = 232;

    private static readonly Size CompactIdleSize = new(128, 34);
    private static readonly Size CompactMediaSize = new(236, 34);
    private static readonly Size ExpandedIdleSize = new(300, 74);
    private static readonly Size ExpandedMediaSize = new(372, 156);
    private static readonly Size ExpandedMediaWithTimelineSize = new(372, 184);
    private static readonly Size ExpandedNotificationSize = new(372, 116);

    // After the user presses play/pause, show the new state at once and ignore stale reports for a moment.
    private static readonly TimeSpan OptimisticWindow = TimeSpan.FromMilliseconds(1500);

    private readonly SettingsService _settings;
    private readonly IMediaService _media;
    private readonly INotificationService _notifications;
    private readonly List<IslandNotification> _notificationQueue = [];
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _tickTimer;

    private MediaSnapshot? _snapshot;
    private string? _trackKey;
    private byte[]? _artworkData;
    private Color _artworkAccent = ArtworkLoader.DefaultAccent;
    private bool _pointerInside;
    private int _actionsInProgress;
    private (bool IsPlaying, DateTime Until)? _optimisticPlayState;

    public IslandViewModel(SettingsService settings, IMediaService media, INotificationService notifications)
    {
        _settings = settings;
        _media = media;
        _notifications = notifications;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.Current.AutoCollapseSeconds) };
        _collapseTimer.Tick += (_, _) => TryAutoCollapse();

        // Drives the clock and progress bar; runs only while the island is expanded.
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick();

        _settings.Changed += OnSettingsChanged;
        _media.Changed += OnMediaChanged;
        _notifications.Received += OnNotificationReceived;

        ApplyAccent();
        ApplyMedia(_media.Current);
        UpdateShape();
    }

    /// <summary>Raised when the user asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when a different song starts, so the view can play a "bump" animation.</summary>
    public event EventHandler? TrackChanged;

    /// <summary>Raised when a notification is shown, so the view can bump the app icon.</summary>
    public event EventHandler? NotificationArrived;

    public IslandSettings Settings => _settings.Current;

    // ---- State -------------------------------------------------------------------------------

    [ObservableProperty] public partial bool IsExpanded { get; private set; }
    [ObservableProperty] public partial bool HasMedia { get; private set; }
    [ObservableProperty] public partial bool HasUnseenActivity { get; private set; }

    [ObservableProperty] public partial double IslandWidth { get; private set; }
    [ObservableProperty] public partial double IslandHeight { get; private set; }
    [ObservableProperty] public partial CornerRadius IslandCornerRadius { get; private set; }

    public bool ShowCompactIdle => !IsExpanded && !HasMedia;
    public bool ShowCompactMedia => !IsExpanded && HasMedia;
    public bool ShowExpandedIdle => IsExpanded && !HasNotification && !HasMedia;
    public bool ShowExpandedMedia => IsExpanded && !HasNotification && HasMedia;
    public bool ShowExpandedNotification => IsExpanded && HasNotification;

    // Only visible animations run; the collapsed, idle island renders nothing at all.
    public bool AnimateCompactVisualizer => ShowCompactMedia && IsPlaying && Settings.AnimatedVisualizer;
    public bool AnimateExpandedVisualizer => ShowExpandedMedia && IsPlaying && Settings.AnimatedVisualizer;
    public bool ShowAmbientGlow => ShowExpandedMedia && IsPlaying && Settings.TintFromArtwork;

    // ---- Media -------------------------------------------------------------------------------

    [ObservableProperty] public partial string MediaTitle { get; private set; } = "";
    [ObservableProperty] public partial string MediaArtist { get; private set; } = "";
    [ObservableProperty] public partial string MediaApp { get; private set; } = "";
    [ObservableProperty] public partial Bitmap? Artwork { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool CanPlayPause { get; private set; }
    [ObservableProperty] public partial bool CanGoNext { get; private set; }
    [ObservableProperty] public partial bool CanGoPrevious { get; private set; }
    [ObservableProperty] public partial Color AccentColor { get; private set; } = ArtworkLoader.DefaultAccent;
    [ObservableProperty] public partial IBrush AccentBrush { get; private set; } = new SolidColorBrush(ArtworkLoader.DefaultAccent);
    [ObservableProperty] public partial IBrush? GlowBrush { get; private set; }

    public bool HasArtwork => Artwork is not null;
    public bool IsPaused => !IsPlaying;

    // ---- Progress ----------------------------------------------------------------------------

    [ObservableProperty] public partial bool HasTimeline { get; private set; }
    [ObservableProperty] public partial double ProgressWidth { get; private set; }
    [ObservableProperty] public partial string ElapsedText { get; private set; } = "";
    [ObservableProperty] public partial string RemainingText { get; private set; } = "";

    /// <summary>False for one update after a jump (new track or seek) so the bar snaps instead of sliding back.</summary>
    [ObservableProperty] public partial bool ProgressSmooth { get; private set; }

    public double ExpandedMediaHeight => HasTimeline ? ExpandedMediaWithTimelineSize.Height : ExpandedMediaSize.Height;

    // ---- Idle content -----------------------------------------------------------------------

    [ObservableProperty] public partial string ClockText { get; private set; } = "";
    [ObservableProperty] public partial string DateText { get; private set; } = "";

    // ---- Interaction -------------------------------------------------------------------------

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
        IsExpanded = false;

        // Seen notifications leave the island (they stay in the Windows notification centre).
        _notificationQueue.Clear();
        HasNotification = false;
    }

    public void Expand(bool peek)
    {
        HasUnseenActivity = false;
        IsExpanded = true;

        // A peek (triggered by an event, not the user) closes on its own unless the user engages.
        if (peek && !_pointerInside)
            RestartCollapseTimer();
    }

    // ---- Notifications -----------------------------------------------------------------------

    [ObservableProperty] public partial bool HasNotification { get; private set; }
    [ObservableProperty] public partial string NotificationApp { get; private set; } = "";
    [ObservableProperty] public partial string NotificationTitle { get; private set; } = "";
    [ObservableProperty] public partial string NotificationBody { get; private set; } = "";
    [ObservableProperty] public partial Bitmap? NotificationIcon { get; private set; }
    [ObservableProperty] public partial string NotificationMoreText { get; private set; } = "";

    public bool HasNotificationIcon => NotificationIcon is not null;
    public bool HasNotificationBody => NotificationBody.Length > 0;
    public bool HasMoreNotifications => NotificationMoreText.Length > 0;
    public string MuteAppToolTip => $"Don't show notifications from {NotificationApp}";

    private IslandNotification? CurrentNotification => _notificationQueue.LastOrDefault();

    private void OnNotificationReceived(object? sender, IslandNotification notification) =>
        Dispatcher.UIThread.Post(() => ShowNotification(notification));

    /// <summary>Shows a notification; also used by the <c>--demo</c> mode.</summary>
    public void ShowNotification(IslandNotification notification)
    {
        if (!Settings.ShowNotifications || Settings.IsMuted(notification.AppName))
            return;

        _notificationQueue.Add(notification);
        if (_notificationQueue.Count > 20)
            _notificationQueue.RemoveAt(0);

        PresentCurrentNotification();
        NotificationArrived?.Invoke(this, EventArgs.Empty);

        if (Settings.ExpandOnNotification || IsExpanded)
        {
            Expand(peek: true);
            if (!_pointerInside)
                RestartCollapseTimer(extraSeconds: 2);
        }
        else
        {
            HasUnseenActivity = true;
        }
    }

    private void PresentCurrentNotification()
    {
        var current = CurrentNotification;
        HasNotification = current is not null;
        if (current is null)
            return;

        var hidePreview = Settings.HideNotificationPreviews;
        NotificationApp = current.AppName;
        NotificationTitle = hidePreview ? "New notification" : current.Title;
        NotificationBody = hidePreview ? "" : current.Body;

        var (icon, _) = ArtworkLoader.Load(current.AppIcon);
        var old = NotificationIcon;
        NotificationIcon = icon;
        if (old is not null)
            DispatcherTimer.RunOnce(old.Dispose, TimeSpan.FromSeconds(1));

        var more = _notificationQueue.Count - 1;
        NotificationMoreText = more > 0 ? $"+{more} more" : "";
        OnPropertyChanged(nameof(HasNotificationBody));
        OnPropertyChanged(nameof(HasMoreNotifications));
        OnPropertyChanged(nameof(MuteAppToolTip));
    }

    [RelayCommand]
    private void OpenNotification()
    {
        if (CurrentNotification is { } notification)
            _notifications.OpenApp(notification);

        Collapse();
    }

    /// <summary>Removes the notification from Windows too, then shows the previous one or closes.</summary>
    [RelayCommand]
    private async Task DismissNotification()
    {
        if (CurrentNotification is not { } notification)
            return;

        _notificationQueue.Remove(notification);
        if (_notificationQueue.Count == 0)
            Collapse();
        else
            PresentCurrentNotification();

        await _notifications.DismissAsync(notification);
    }

    [RelayCommand]
    private void MuteNotificationApp()
    {
        if (CurrentNotification is not { } notification)
            return;

        if (!Settings.IsMuted(notification.AppName))
            _settings.Update(Settings with { MutedNotificationApps = [.. Settings.MutedNotificationApps, notification.AppName] });

        _notificationQueue.RemoveAll(n => n.AppName.Equals(notification.AppName, StringComparison.OrdinalIgnoreCase));
        if (_notificationQueue.Count == 0)
            Collapse();
        else
            PresentCurrentNotification();
    }

    public void OnPointerEntered()
    {
        _pointerInside = true;
        _collapseTimer.Stop();
    }

    public void OnPointerExited()
    {
        _pointerInside = false;
        if (IsExpanded)
            RestartCollapseTimer();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Collapse();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private Task PlayPause()
    {
        // Flip the icon immediately; the player's confirmation arrives a moment later.
        _optimisticPlayState = (!IsPlaying, DateTime.UtcNow + OptimisticWindow);
        IsPlaying = !IsPlaying;
        UpdateProgress(smooth: false);
        return RunMediaAction(_media.TogglePlayPauseAsync);
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task Next() => RunMediaAction(_media.NextAsync);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task Previous() => RunMediaAction(_media.PreviousAsync);

    private async Task RunMediaAction(Func<Task> action)
    {
        _actionsInProgress++;
        try
        {
            await action();
        }
        catch (Exception)
        {
            // The player may have closed between the click and the call; the next update corrects the UI.
            _optimisticPlayState = null;
            ApplyMedia(_media.Current);
        }
        finally
        {
            _actionsInProgress--;
        }
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

    // ---- Updates -----------------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, IslandSettings settings)
    {
        OnPropertyChanged(nameof(Settings));
        ApplyAccent();
        ApplyMedia(_media.Current);
        NotifyAnimations();
    }

    private void OnMediaChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => ApplyMedia(_media.Current));

    private void ApplyMedia(MediaSnapshot? media)
    {
        if (!Settings.ShowMedia)
            media = null;

        var previousKey = _trackKey;
        _snapshot = media;
        HasMedia = media is not null;

        if (media is null)
        {
            _trackKey = null;
            _optimisticPlayState = null;
            IsPlaying = false;
            HasTimeline = false;
            return;
        }

        MediaTitle = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title;
        MediaArtist = media.Artist;
        MediaApp = media.AppName;
        CanPlayPause = media.CanPlayPause;
        CanGoNext = media.CanGoNext;
        CanGoPrevious = media.CanGoPrevious;
        _trackKey = media.TrackKey;

        // Keep the optimistic state until the player agrees or the window expires.
        if (_optimisticPlayState is { } pending && DateTime.UtcNow < pending.Until && media.IsPlaying != pending.IsPlaying)
            IsPlaying = pending.IsPlaying;
        else
        {
            _optimisticPlayState = null;
            IsPlaying = media.IsPlaying;
        }

        if (!ReferenceEquals(media.Artwork, _artworkData))
        {
            _artworkData = media.Artwork;
            var (image, accent) = ArtworkLoader.Load(media.Artwork);
            ReplaceArtwork(image);
            _artworkAccent = accent;
            ApplyAccent();
        }

        var trackChanged = previousKey is not null && previousKey != _trackKey;
        HasTimeline = media.Duration is not null;
        UpdateProgress(smooth: !trackChanged && previousKey is not null);

        if (trackChanged)
        {
            TrackChanged?.Invoke(this, EventArgs.Empty);

            // Briefly show a new track, the way iOS does.
            if (!IsExpanded)
            {
                if (Settings.ExpandOnTrackChange)
                    Expand(peek: true);
                else
                    HasUnseenActivity = true;
            }
        }
    }

    /// <summary>Swaps artwork, disposing the old image only after the cross-fade has finished with it.</summary>
    private void ReplaceArtwork(Bitmap? image)
    {
        var old = Artwork;
        Artwork = image;
        if (old is not null)
            DispatcherTimer.RunOnce(old.Dispose, TimeSpan.FromSeconds(1));
    }

    private void ApplyAccent()
    {
        AccentColor = Settings.TintFromArtwork ? _artworkAccent : ArtworkLoader.DefaultAccent;
        AccentBrush = new SolidColorBrush(AccentColor);

        // A soft pool of the artwork's colour behind the album art.
        GlowBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.16, 0.3, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.16, 0.3, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x60, AccentColor.R, AccentColor.G, AccentColor.B), 0),
                new GradientStop(Color.FromArgb(0x1C, AccentColor.R, AccentColor.G, AccentColor.B), 0.55),
                new GradientStop(Color.FromArgb(0x00, AccentColor.R, AccentColor.G, AccentColor.B), 1),
            },
        };
    }

    private void UpdateProgress(bool smooth)
    {
        if (_snapshot?.Duration is not { } duration)
            return;

        var position = _snapshot.PositionAt(DateTimeOffset.Now);
        var width = ProgressTrackWidth * Math.Clamp(position / duration, 0, 1);

        // Large jumps (seek, new track) snap; normal ticks glide for one second.
        ProgressSmooth = smooth && Math.Abs(width - ProgressWidth) < ProgressTrackWidth * 0.1;
        ProgressWidth = width;
        ElapsedText = Format(position);
        RemainingText = "-" + Format(duration - position);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void UpdateShape()
    {
        var size = (IsExpanded, HasMedia) switch
        {
            (true, _) when HasNotification => ExpandedNotificationSize,
            (true, true) => HasTimeline ? ExpandedMediaWithTimelineSize : ExpandedMediaSize,
            (true, false) => ExpandedIdleSize,
            (false, true) => CompactMediaSize,
            (false, false) => CompactIdleSize,
        };

        IslandWidth = size.Width;
        IslandHeight = size.Height;
        IslandCornerRadius = new CornerRadius(IsExpanded ? (HasMedia || HasNotification ? 40 : 30) : size.Height / 2);

        OnPropertyChanged(nameof(ShowCompactIdle));
        OnPropertyChanged(nameof(ShowCompactMedia));
        OnPropertyChanged(nameof(ShowExpandedIdle));
        OnPropertyChanged(nameof(ShowExpandedMedia));
        OnPropertyChanged(nameof(ShowExpandedNotification));
        NotifyAnimations();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("t");
        DateText = now.ToString("dddd, MMMM d");

        if (ShowExpandedMedia && IsPlaying)
            UpdateProgress(smooth: true);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        UpdateShape();

        // The ticker only runs while its output is visible, so the collapsed island does no work at all.
        if (value)
        {
            UpdateProgress(smooth: false);
            Tick();
            _tickTimer.Start();
        }
        else
        {
            _tickTimer.Stop();
        }
    }

    partial void OnHasMediaChanged(bool value) => UpdateShape();
    partial void OnHasNotificationChanged(bool value) => UpdateShape();
    partial void OnNotificationIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasNotificationIcon));
    partial void OnHasTimelineChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandedMediaHeight));
        UpdateShape();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPaused));
        NotifyAnimations();
    }

    private void NotifyAnimations()
    {
        OnPropertyChanged(nameof(AnimateCompactVisualizer));
        OnPropertyChanged(nameof(AnimateExpandedVisualizer));
        OnPropertyChanged(nameof(ShowAmbientGlow));
    }

    partial void OnArtworkChanged(Bitmap? value) => OnPropertyChanged(nameof(HasArtwork));
    partial void OnCanPlayPauseChanged(bool value) => PlayPauseCommand.NotifyCanExecuteChanged();
    partial void OnCanGoNextChanged(bool value) => NextCommand.NotifyCanExecuteChanged();
    partial void OnCanGoPreviousChanged(bool value) => PreviousCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        _collapseTimer.Stop();
        _tickTimer.Stop();
        _settings.Changed -= OnSettingsChanged;
        _media.Changed -= OnMediaChanged;
        _notifications.Received -= OnNotificationReceived;
        Artwork?.Dispose();
        NotificationIcon?.Dispose();
    }
}
