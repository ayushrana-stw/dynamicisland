using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Media;

namespace DynamicIsland.App.ViewModels;

public sealed partial class IslandViewModel
{
    /// <summary>Width of the progress track in the expanded media view.</summary>
    public const double ProgressTrackWidth = 232;

    // After the user presses play/pause, show the new state at once and ignore stale reports for a moment.
    private static readonly TimeSpan OptimisticWindow = TimeSpan.FromMilliseconds(1500);

    private readonly IMediaService _media;
    private MediaSnapshot? _snapshot;
    private string? _trackKey;
    private byte[]? _artworkData;
    private Color _artworkAccent = ArtworkLoader.DefaultAccent;
    private (bool IsPlaying, DateTime Until)? _optimisticPlayState;

    [ObservableProperty] public partial bool HasMedia { get; private set; }
    [ObservableProperty] public partial string MediaTitle { get; private set; } = "";
    [ObservableProperty] public partial string MediaArtist { get; private set; } = "";
    [ObservableProperty] public partial string MediaApp { get; private set; } = "";
    [ObservableProperty] public partial Bitmap? Artwork { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool CanPlayPause { get; private set; }
    [ObservableProperty] public partial bool CanGoNext { get; private set; }
    [ObservableProperty] public partial bool CanGoPrevious { get; private set; }
    [ObservableProperty] public partial bool CanSeek { get; private set; }
    [ObservableProperty] public partial Color AccentColor { get; private set; } = ArtworkLoader.DefaultAccent;
    [ObservableProperty] public partial IBrush AccentBrush { get; private set; } = new SolidColorBrush(ArtworkLoader.DefaultAccent);
    [ObservableProperty] public partial IBrush? GlowBrush { get; private set; }

    public bool HasArtwork => Artwork is not null;
    public bool IsPaused => !IsPlaying;

    // Only visible animations run; the collapsed, idle island renders nothing at all.
    public bool AnimateCompactVisualizer => ShowCompactMedia && IsPlaying && Settings.AnimatedVisualizer;
    public bool AnimateExpandedVisualizer => ShowExpandedMedia && IsPlaying && Settings.AnimatedVisualizer;
    public bool ShowAmbientGlow => ShowExpandedMedia && IsPlaying && Settings.TintFromArtwork;
    public bool AnimateMarquee => ShowExpandedMedia;
    public bool AnimateHoverMarquee => ShowHoverTitle;

    // ---- Progress ----------------------------------------------------------------------------

    [ObservableProperty] public partial bool HasTimeline { get; private set; }
    [ObservableProperty] public partial double ProgressWidth { get; private set; }
    [ObservableProperty] public partial string ElapsedText { get; private set; } = "";
    [ObservableProperty] public partial string RemainingText { get; private set; } = "";
    [ObservableProperty] public partial bool IsScrubbing { get; private set; }

    /// <summary>False for one update after a jump (new track or seek) so the bar snaps instead of sliding back.</summary>
    [ObservableProperty] public partial bool ProgressSmooth { get; private set; }

    public double ExpandedMediaHeight => SizeFor(IslandMode.ExpandedMedia).Height;

    // ---- Commands ----------------------------------------------------------------------------

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

    // ---- Scrubbing (drag the progress bar) ------------------------------------------------------

    public void BeginScrub(double fraction)
    {
        if (!CanSeek || _snapshot?.Duration is null)
            return;

        IsScrubbing = true;
        _actionsInProgress++;
        ShowScrubPosition(fraction);
    }

    public void UpdateScrub(double fraction)
    {
        if (IsScrubbing)
            ShowScrubPosition(fraction);
    }

    public async void EndScrub(double fraction)
    {
        if (!IsScrubbing || _snapshot?.Duration is not { } duration)
            return;

        ShowScrubPosition(fraction);
        IsScrubbing = false;
        _actionsInProgress--;

        var target = duration * Math.Clamp(fraction, 0, 1);

        // Assume the seek works so the bar does not jump back while the player catches up.
        _snapshot = _snapshot with { Position = target, PositionTimestamp = DateTimeOffset.Now };
        await RunMediaAction(() => _media.SeekAsync(target));
    }

    private void ShowScrubPosition(double fraction)
    {
        if (_snapshot?.Duration is not { } duration)
            return;

        fraction = Math.Clamp(fraction, 0, 1);
        var position = duration * fraction;
        ProgressSmooth = false;
        ProgressWidth = ProgressTrackWidth * fraction;
        ElapsedText = Format(position);
        RemainingText = "-" + Format(duration - position);
    }

    // ---- Updates -----------------------------------------------------------------------------

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
        CanSeek = media.CanSeek && media.Duration is not null;
        _trackKey = media.TrackKey;

        // Keep the optimistic state until the player agrees or the window expires.
        if (_optimisticPlayState is { } pending && DateTime.UtcNow < pending.Until && media.IsPlaying != pending.IsPlaying)
        {
            IsPlaying = pending.IsPlaying;
        }
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
        if (IsScrubbing || _snapshot?.Duration is not { } duration)
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

    partial void OnHasMediaChanged(bool value) => UpdateShape();

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

    partial void OnArtworkChanged(Bitmap? value) => OnPropertyChanged(nameof(HasArtwork));
    partial void OnCanPlayPauseChanged(bool value) => PlayPauseCommand.NotifyCanExecuteChanged();
    partial void OnCanGoNextChanged(bool value) => NextCommand.NotifyCanExecuteChanged();
    partial void OnCanGoPreviousChanged(bool value) => PreviousCommand.NotifyCanExecuteChanged();
}
