using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Dev;

/// <summary>
/// <c>--snapshot &lt;folder&gt;</c>: renders the island in each state to PNG files, then exits.
/// Lets the design be reviewed without screen-capturing the desktop.
/// </summary>
internal static class Snapshotter
{
    public static async Task RunAsync(Window window, IslandViewModel viewModel, string folder)
    {
        Directory.CreateDirectory(folder);
        var settle = TimeSpan.FromMilliseconds(1200);

        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "1-compact.png"));

        viewModel.Expand(peek: false);
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "2-expanded.png"));

        viewModel.PlayPauseCommand.Execute(null);
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "3-paused.png"));

        viewModel.NextCommand.Execute(null);
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        Save(window, Path.Combine(folder, "4-track-change-mid.png"));
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "5-next-track.png"));

        viewModel.Collapse();
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "6-collapsed-paused.png"));

        viewModel.ShowNotification(DemoNotifications.Teams());
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "7-notification.png"));

        viewModel.ShowNotification(DemoNotifications.Outlook());
        await Task.Delay(settle);
        Save(window, Path.Combine(folder, "8-second-notification.png"));
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not Control root)
            return;

        const double scale = 2;
        var size = new PixelSize((int)(root.Bounds.Width * scale), (int)(root.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(root);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
