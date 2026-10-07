namespace DynamicIsland.Core.Media;

/// <summary>A point-in-time view of the active media session.</summary>
/// <param name="Artwork">Encoded image bytes (PNG/JPEG), or null when the player provides none.</param>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string AppName,
    byte[]? Artwork,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanGoNext,
    bool CanGoPrevious)
{
    /// <summary>Track length, or null when the player does not report a timeline.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Playback position as of <see cref="PositionTimestamp"/>.</summary>
    public TimeSpan Position { get; init; }

    public DateTimeOffset PositionTimestamp { get; init; }

    /// <summary>Identifies the track; changes when the song changes but not on play/pause.</summary>
    public string TrackKey => $"{AppName}\u001f{Title}\u001f{Artist}";

    /// <summary>Position now, extrapolated while playing because players only report it occasionally.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = IsPlaying ? Position + (now - PositionTimestamp) : Position;
        if (position < TimeSpan.Zero)
            return TimeSpan.Zero;
        return Duration is { } duration && position > duration ? duration : position;
    }
}

/// <summary>Platform media integration (Windows: GSMTC, macOS: MediaRemote/AppleScript).</summary>
public interface IMediaService : IDisposable
{
    /// <summary>The current session, or null when nothing is playing or paused.</summary>
    MediaSnapshot? Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> changes. May be raised on any thread.</summary>
    event EventHandler? Changed;

    Task StartAsync();
    Task TogglePlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
}

/// <summary>Used on platforms without media integration yet.</summary>
public sealed class NullMediaService : IMediaService
{
    public MediaSnapshot? Current => null;
    public event EventHandler? Changed { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
    public Task TogglePlayPauseAsync() => Task.CompletedTask;
    public Task NextAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask;
    public void Dispose() { }
}
