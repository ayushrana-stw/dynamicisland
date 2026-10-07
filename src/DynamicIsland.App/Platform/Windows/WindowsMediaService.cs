using DynamicIsland.Core.Media;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace DynamicIsland.App.Platform.Windows;

/// <summary>
/// Reads the active media session through Windows' Global System Media Transport Controls,
/// the same source the volume flyout uses. Event-driven: no polling.
/// </summary>
internal sealed class WindowsMediaService : IMediaService
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SessionManager? _manager;
    private Session? _session;

    // Artwork is re-read only when the player reports new media properties, never on play/pause.
    private byte[]? _artwork;
    private bool _artworkStale = true;

    public MediaSnapshot? Current { get; private set; }

    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        _manager = await SessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await AttachToCurrentSessionAsync();
    }

    public Task TogglePlayPauseAsync() => _session?.TryTogglePlayPauseAsync().AsTask() ?? Task.CompletedTask;
    public Task NextAsync() => _session?.TrySkipNextAsync().AsTask() ?? Task.CompletedTask;
    public Task PreviousAsync() => _session?.TrySkipPreviousAsync().AsTask() ?? Task.CompletedTask;

    private void OnCurrentSessionChanged(SessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = AttachToCurrentSessionAsync();

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args)
    {
        // Players often send the new title first and the new thumbnail moments later,
        // so every properties change re-reads the artwork.
        _artworkStale = true;
        _ = RefreshAsync();
        _ = RecheckArtworkAsync(sender);
    }

    /// <summary>Some players never send a second event when the thumbnail catches up, so look once more.</summary>
    private async Task RecheckArtworkAsync(Session session)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        if (!ReferenceEquals(session, _session))
            return;

        _artworkStale = true;
        await RefreshAsync();
    }

    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
    private void OnTimelinePropertiesChanged(Session sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync();

    private async Task AttachToCurrentSessionAsync()
    {
        var session = _manager?.GetCurrentSession();
        if (!ReferenceEquals(session, _session))
        {
            Detach();

            _session = session;
            _artworkStale = true;

            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            Current = await ReadSnapshotAsync(_session);
        }
        catch (Exception)
        {
            // Sessions can vanish mid-read when a player closes.
            Current = null;
        }
        finally
        {
            _refreshGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<MediaSnapshot?> ReadSnapshotAsync(Session? session)
    {
        if (session is null)
            return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        if (properties is null || playback is null)
            return null;

        var status = playback.PlaybackStatus;
        if (status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed
            or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
            return null;

        if (string.IsNullOrWhiteSpace(properties.Title) && string.IsNullOrWhiteSpace(properties.Artist))
            return null;

        var appName = FriendlyAppName(session.SourceAppUserModelId);
        var artist = string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;

        if (_artworkStale)
        {
            _artworkStale = false;
            var artwork = await ReadThumbnailAsync(properties);

            // Keep the same array when the image is unchanged so the UI does not redecode it.
            if (artwork is null || _artwork is null || !artwork.AsSpan().SequenceEqual(_artwork))
                _artwork = artwork;
        }

        var timeline = session.GetTimelineProperties();
        var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;

        var controls = playback.Controls;
        return new MediaSnapshot(
            properties.Title ?? "",
            artist,
            appName,
            _artwork,
            status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
            controls.IsNextEnabled,
            controls.IsPreviousEnabled)
        {
            Duration = duration > TimeSpan.FromSeconds(1) ? duration : null,
            Position = timeline is null ? TimeSpan.Zero : timeline.Position - timeline.StartTime,
            PositionTimestamp = timeline is null || timeline.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : timeline.LastUpdatedTime,
        };
    }

    private static async Task<byte[]?> ReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (properties.Thumbnail is null)
            return null;

        try
        {
            using var winrtStream = await properties.Thumbnail.OpenReadAsync();
            await using var stream = winrtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.Length > 0 ? buffer.ToArray() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Turns "Spotify.exe" or "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" into "Spotify".</summary>
    private static string FriendlyAppName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
            return "";

        var name = appUserModelId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0)
            name = name[(bang + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name switch
        {
            "chrome" => "Chrome",
            "msedge" or "MSEdge" => "Edge",
            "firefox" => "Firefox",
            "ZuneMusic" or "Microsoft.ZuneMusic" => "Media Player",
            _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name,
        };
    }

    public void Dispose()
    {
        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;

        Detach();
        _refreshGate.Dispose();
    }

    private void Detach()
    {
        if (_session is null)
            return;

        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }
}
