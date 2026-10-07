using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicIsland.App.ViewModels;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.Dev;

/// <summary>
/// <c>--snapshot &lt;folder&gt;</c>: walks the island through its states, saves each as a PNG, then exits.
/// Lets the design be reviewed without screen-capturing the desktop.
/// </summary>
internal static class Snapshotter
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1100);

    public static async Task RunAsync(Window window, IslandViewModel vm, SettingsService settings, string folder)
    {
        Directory.CreateDirectory(folder);
        var original = settings.Current;
        var step = 0;

        async Task Shot(string name, Action? action = null, TimeSpan? wait = null)
        {
            action?.Invoke();
            await Task.Delay(wait ?? Settle);
            Save(window, Path.Combine(folder, $"{++step:00}-{name}.png"));
        }

        try
        {
            // Media
            await Shot("compact-media");
            await Shot("hover-title", vm.OnPointerEntered);
            vm.OnPointerExited();
            await Shot("expanded-media", () => vm.Expand(peek: false));
            await Shot("scrubbing", () => vm.BeginScrub(0.7), TimeSpan.FromMilliseconds(400));
            vm.EndScrub(0.7);
            await Shot("paused", () => vm.PlayPauseCommand.Execute(null));
            vm.PlayPauseCommand.Execute(null);

            // HUDs
            vm.Collapse();
            await Task.Delay(Settle);
            await Shot("hud-volume", () => vm.PresentHud("IconVolume", Brushes.WhiteSmoke, 0.62, "62", TimeSpan.FromSeconds(5)), TimeSpan.FromMilliseconds(700));
            await Shot("hud-charging", () => vm.PresentHud("IconBatteryCharging", new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)), 0.8, "Charging · 80%", TimeSpan.FromSeconds(2)), TimeSpan.FromMilliseconds(700));
            await Task.Delay(TimeSpan.FromSeconds(2.2));

            // Timer + split island
            vm.StartTimer("5");
            vm.Collapse();
            await Shot("split-media-timer");
            await Shot("split-plus-mic-dot", () => vm.ApplyPrivacy([new PrivacyUse("Teams", true, false)]));
            await Shot("expanded-timer", () => vm.OpenTimerPickerCommand.Execute(null));
            vm.StopTimer();
            vm.Collapse();
            await Shot("split-mic", null);
            await Shot("split-camera", () => vm.ApplyPrivacy([new PrivacyUse("Teams", true, true)]));

            // Without media: idle, timer picker, history
            settings.Update(settings.Current with { ShowMedia = false });
            await Shot("compact-idle-hover", vm.OnPointerEntered);
            vm.OnPointerExited();
            await Shot("expanded-idle", () => vm.Expand(peek: false));
            vm.ApplyPrivacy([]);
            await Shot("timer-picker", () => vm.OpenTimerPickerCommand.Execute(null));
            await Shot("timer-running", () => vm.StartTimer("25"));
            vm.StopTimer();
            vm.Collapse();
            await Task.Delay(Settle);
            vm.ShowNotification(DemoNotifications.Teams());
            await Shot("notification");
            vm.ShowNotification(DemoNotifications.Outlook());
            await Task.Delay(Settle);
            vm.Collapse();
            await Shot("history", () => vm.OpenHistoryCommand.Execute(null));
            await Shot("compact-timer", () =>
            {
                vm.StartStopwatch();
                vm.Collapse();
            });
            vm.StopTimer();
        }
        finally
        {
            settings.Update(original);
        }
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
