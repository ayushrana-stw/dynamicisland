using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DynamicIsland.App.ViewModels;

internal static class ArtworkLoader
{
    public static readonly Color DefaultAccent = Color.FromRgb(0xF2, 0xF2, 0xF7);

    /// <summary>Decodes artwork at display size and extracts a vivid accent colour from it.</summary>
    public static (Bitmap? Image, Color Accent) Load(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return (null, DefaultAccent);

        try
        {
            using var displayStream = new MemoryStream(data, writable: false);
            var image = Bitmap.DecodeToWidth(displayStream, 192, BitmapInterpolationMode.HighQuality);

            using var sampleStream = new MemoryStream(data, writable: false);
            using var sample = WriteableBitmap.DecodeToWidth(sampleStream, 24, BitmapInterpolationMode.LowQuality);
            return (image, ExtractAccent(sample));
        }
        catch (Exception)
        {
            // Unsupported or corrupt image data: show the placeholder instead.
            return (null, DefaultAccent);
        }
    }

    private static Color ExtractAccent(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var isRgba = buffer.Format == PixelFormat.Rgba8888;
        var width = buffer.Size.Width;
        var height = buffer.Size.Height;
        var row = new byte[width * 4];

        double r = 0, g = 0, b = 0, totalWeight = 0;
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                double pr = isRgba ? row[i] : row[i + 2];
                double pg = row[i + 1];
                double pb = isRgba ? row[i + 2] : row[i];

                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var saturation = max == 0 ? 0 : (max - min) / max;

                // Favour colourful, mid-bright pixels so the accent is not a muddy grey.
                var weight = 0.05 + saturation * saturation * (max / 255.0);
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0)
            return DefaultAccent;

        var hsl = Color.FromRgb((byte)(r / totalWeight), (byte)(g / totalWeight), (byte)(b / totalWeight)).ToHsl();

        // Keep it readable on the black island.
        return new HslColor(1, hsl.H, Math.Min(1, hsl.S * 1.15), Math.Clamp(hsl.L, 0.62, 0.78)).ToRgb();
    }
}
