using Android.Content;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Media;

namespace DynamicIsland.Droid.Platform;

/// <summary>
/// What's playing in any app (Spotify, YouTube Music, podcasts...) through Android's media sessions.
/// Android only shares other apps' sessions with an app that has notification access.
/// </summary>
internal sealed class AndroidMediaService : IMediaService
{
    private const int ArtworkSize = 256;

    private readonly Context _context;
    private readonly MediaSessionManager _manager;
    private readonly ComponentName _listenerComponent;
    private readonly Handler _mainThread = new(Looper.MainLooper!);
    private readonly SessionsListener _sessionsListener;
    private readonly List<(MediaController Controller, ControllerCallback Callback)> _watched = [];

    private MediaController? _active;
    private bool _listening;
    private string? _artworkTrackKey;
    private byte[]? _artwork;

    public AndroidMediaService(Context context)
    {
        _context = context;
        _manager = (MediaSessionManager)context.GetSystemService(Context.MediaSessionService)!;
        _listenerComponent = new ComponentName(context, Java.Lang.Class.FromType(typeof(IslandNotificationListener)));
        _sessionsListener = new SessionsListener(Watch);
    }

    public MediaSnapshot? Current { get; private set; }

    public event EventHandler? Changed;

    public Task StartAsync()
    {
        TryListen();

        // Access may be granted later; the listener connecting is the signal to try again.
        IslandNotificationListener.ConnectionChanged += OnListenerConnectionChanged;
        return Task.CompletedTask;
    }

    private void OnListenerConnectionChanged(object? sender, EventArgs e) => _mainThread.Post(TryListen);

    private void TryListen()
    {
        try
        {
            if (!_listening)
            {
                _manager.AddOnActiveSessionsChangedListener(_sessionsListener, _listenerComponent, _mainThread);
                _listening = true;
            }

            Watch(_manager.GetActiveSessions(_listenerComponent));
        }
        catch (Java.Lang.SecurityException)
        {
            // No notification access yet.
            DebugLog.Write("Android media: waiting for notification access");
        }
    }

    /// <summary>Follows every active session so whichever starts playing can take over the island.</summary>
    private void Watch(IList<MediaController>? controllers)
    {
        foreach (var (controller, callback) in _watched)
            controller.UnregisterCallback(callback);
        _watched.Clear();

        foreach (var controller in controllers ?? [])
        {
            var callback = new ControllerCallback(Refresh);
            controller.RegisterCallback(callback, _mainThread);
            _watched.Add((controller, callback));
        }

        Refresh();
    }

    /// <summary>The playing session wins; otherwise the most recent paused one (Android lists newest first).</summary>
    private void Refresh()
    {
        _active = _watched.Select(w => w.Controller).FirstOrDefault(c => IsPlayingState(c.PlaybackState))
            ?? _watched.Select(w => w.Controller).FirstOrDefault(c => c.PlaybackState?.State is PlaybackStateCode.Paused);

        Current = _active is null ? null : Snapshot(_active);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPlayingState(PlaybackState? state) =>
        state?.State is PlaybackStateCode.Playing or PlaybackStateCode.Buffering or PlaybackStateCode.Connecting;

    private MediaSnapshot? Snapshot(MediaController controller)
    {
        var metadata = controller.Metadata;
        var state = controller.PlaybackState;
        var title = metadata?.GetString(MediaMetadata.MetadataKeyTitle)
            ?? metadata?.GetString(MediaMetadata.MetadataKeyDisplayTitle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var artist = metadata?.GetString(MediaMetadata.MetadataKeyArtist)
            ?? metadata?.GetString(MediaMetadata.MetadataKeyAlbumArtist)
            ?? metadata?.GetString(MediaMetadata.MetadataKeyDisplaySubtitle)
            ?? "";
        var appName = AppInfo.Label(_context, controller.PackageName ?? "");

        var actions = state?.Actions ?? 0;
        bool Has(long action) => (actions & action) != 0;

        var durationMs = metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0;
        var snapshot = new MediaSnapshot(
            title,
            artist,
            appName,
            Artwork(metadata, $"{appName}\u001f{title}\u001f{artist}"),
            IsPlaying: IsPlayingState(state),
            // Many players leave the play/pause flags out but still respond to them.
            CanPlayPause: actions == 0 || Has(PlaybackState.ActionPlayPause | PlaybackState.ActionPlay | PlaybackState.ActionPause),
            CanGoNext: Has(PlaybackState.ActionSkipToNext),
            CanGoPrevious: Has(PlaybackState.ActionSkipToPrevious))
        {
            Duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : null,
            CanSeek = durationMs > 0 && Has(PlaybackState.ActionSeekTo),
        };

        if (state is null)
            return snapshot;

        // Android stamps the position with the uptime clock; convert it to wall-clock time.
        var age = TimeSpan.FromMilliseconds(Math.Max(0, SystemClock.ElapsedRealtime() - state.LastPositionUpdateTime));
        return snapshot with
        {
            Position = TimeSpan.FromMilliseconds(Math.Max(0, state.Position)),
            PositionTimestamp = DateTimeOffset.Now - age,
        };
    }

    /// <summary>Album art as PNG, re-encoded only when the track changes.</summary>
    private byte[]? Artwork(MediaMetadata? metadata, string trackKey)
    {
        if (trackKey == _artworkTrackKey && _artwork is not null)
            return _artwork;

        var bitmap = metadata?.GetBitmap(MediaMetadata.MetadataKeyAlbumArt)
            ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyArt)
            ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyDisplayIcon);

        _artworkTrackKey = trackKey;
        _artwork = Images.ToPng(bitmap, ArtworkSize);
        return _artwork;
    }

    public Task TogglePlayPauseAsync()
    {
        if (_active?.GetTransportControls() is { } controls)
        {
            if (IsPlayingState(_active.PlaybackState))
                controls.Pause();
            else
                controls.Play();
        }

        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        _active?.GetTransportControls().SkipToNext();
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        _active?.GetTransportControls().SkipToPrevious();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _active?.GetTransportControls().SeekTo((long)position.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        IslandNotificationListener.ConnectionChanged -= OnListenerConnectionChanged;
        if (_listening)
            _manager.RemoveOnActiveSessionsChangedListener(_sessionsListener);

        foreach (var (controller, callback) in _watched)
            controller.UnregisterCallback(callback);
        _watched.Clear();
    }

    private sealed class SessionsListener(Action<IList<MediaController>?> changed)
        : Java.Lang.Object, MediaSessionManager.IOnActiveSessionsChangedListener
    {
        public void OnActiveSessionsChanged(IList<MediaController>? controllers) => changed(controllers);
    }

    private sealed class ControllerCallback(Action changed) : MediaController.Callback
    {
        public override void OnPlaybackStateChanged(PlaybackState? state) => changed();
        public override void OnMetadataChanged(MediaMetadata? metadata) => changed();
        public override void OnSessionDestroyed() => changed();
    }
}
