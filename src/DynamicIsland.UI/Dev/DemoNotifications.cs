using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.UI.Dev;

/// <summary>Sample notifications for <c>--demo</c> and <c>--snapshot</c>.</summary>
public static class DemoNotifications
{
    private static uint _nextId = 1;

    public static IslandNotification Teams() => Create(
        "Teams", Color.FromRgb(0x50, 0x59, 0xC9), "T",
        "Alex Morgan","Can you share the build link before the 3 pm sync? I want to try the new island.");

    public static IslandNotification Outlook() => Create(
        "Outlook", Color.FromRgb(0x0F, 0x6C, 0xBD), "O",
        "Design review moved to Thursday", "The meeting has been rescheduled. Open the invite for the new time.");

    private static IslandNotification Create(string app, Color tile, string letter, string title, string body) =>
        new(_nextId++, "", app, title, body, RenderIcon(tile, letter), DateTimeOffset.Now);

    private static byte[] RenderIcon(Color tile, string letter)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(new SolidColorBrush(tile), null, new RoundedRect(new Rect(0, 0, 64, 64), 14));
            var text = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.Bold), 34, Brushes.White);
            context.DrawText(text, new Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
