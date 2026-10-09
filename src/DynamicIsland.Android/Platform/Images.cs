using Android.Graphics;
using Android.Graphics.Drawables;

namespace DynamicIsland.Droid.Platform;

/// <summary>Turns Android images into the encoded bytes the shared island UI expects.</summary>
internal static class Images
{
    /// <summary>PNG of <paramref name="bitmap"/>, scaled down so its longer side is at most <paramref name="maxSize"/>.</summary>
    public static byte[]? ToPng(Bitmap? bitmap, int maxSize)
    {
        if (bitmap is null || bitmap.IsRecycled || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        var scale = Math.Min(1.0, (double)maxSize / Math.Max(bitmap.Width, bitmap.Height));
        var scaled = scale < 1
            ? Bitmap.CreateScaledBitmap(bitmap, Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true)
            : bitmap;

        try
        {
            using var stream = new MemoryStream();
            return scaled.Compress(Bitmap.CompressFormat.Png!, 100, stream) ? stream.ToArray() : null;
        }
        finally
        {
            if (scaled != bitmap)
                scaled.Recycle();
        }
    }

    /// <summary>PNG of a drawable (such as an app icon) rendered at <paramref name="size"/> pixels square.</summary>
    public static byte[]? ToPng(Drawable? drawable, int size)
    {
        if (drawable is null)
            return null;

        using var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(bitmap))
        {
            drawable.SetBounds(0, 0, size, size);
            drawable.Draw(canvas);
        }

        var png = ToPng(bitmap, size);
        bitmap.Recycle();
        return png;
    }
}
