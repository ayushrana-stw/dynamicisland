using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicIsland.Core.Media;

namespace DynamicIsland.App.Dev;

/// <summary>A fake player for UI work (<c>--demo</c>), so the island can be designed without real media.</summary>
internal sealed class DemoMediaService : IMediaService
{
    private static readonly (string Title, string Artist, Color From, Color To)[] Tracks =
    [
        ("Midnight City", "M83", Color.FromRgb(0xFF, 0x3C, 0xAC), Color.FromRgb(0x2B, 0x86, 0xC5)),
        ("Blinding Lights", "The Weeknd", Color.FromRgb(0xF8, 0x36, 0x00), Color.FromRgb(0xF9, 0xD4, 0x23)),
        ("Ocean Eyes", "Billie Eilish", Color.FromRgb(0x0B, 0xA3, 0x60), Color.FromRgb(0x3C, 0xBA, 0x92)),
    ];

    private int _index;
    private bool _playing = true;
    private readonly Dictionary<int, byte[]> _artwork = [];

    public MediaSnapshot? Current { get; private set; }
    public event EventHandler? Changed;

    public Task StartAsync()
    {
        Publish();
        return Task.CompletedTask;
    }

    public Task TogglePlayPauseAsync()
    {
        _playing = !_playing;
        Publish();
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        _index = (_index + 1) % Tracks.Length;
        Publish(newTrack: true);
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        _index = (_index + Tracks.Length - 1) % Tracks.Length;
        Publish(newTrack: true);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _seekTo = position;
        Publish();
        return Task.CompletedTask;
    }

    private TimeSpan? _seekTo;

    private void Publish(bool newTrack = false)
    {
        var track = Tracks[_index];
        if (!_artwork.TryGetValue(_index, out var art))
            _artwork[_index] = art = RenderArtwork(track.From, track.To);

        var now = DateTimeOffset.Now;
        var position = _seekTo ?? (newTrack || Current is null ? TimeSpan.FromSeconds(47) : Current.PositionAt(now));
        _seekTo = null;

        Current = new MediaSnapshot(track.Title, track.Artist, "Demo Player", art, _playing, true, true, true)
        {
            Duration = TimeSpan.FromSeconds(223),
            Position = position,
            PositionTimestamp = now,
            CanSeek = true,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static byte[] RenderArtwork(Color from, Color to)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
            };
            context.DrawRectangle(gradient, null, new Rect(0, 0, 256, 256));
            context.DrawEllipse(new SolidColorBrush(Colors.White, 0.18), null, new Point(170, 90), 70, 70);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public void Dispose() { }
}
