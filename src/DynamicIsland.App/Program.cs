using Avalonia;

namespace DynamicIsland.App;

internal static class Program
{
    private const string OpenSettingsSignalName = "DynamicIsland.OpenSettings";

    [STAThread]
    public static void Main(string[] args)
    {
        // Only one island per user session. Launching it again opens Settings in the running copy.
        using var mutex = new Mutex(initiallyOwned: true, "DynamicIsland.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (EventWaitHandle.TryOpenExisting(OpenSettingsSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }

            return;
        }

        using var openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsSignalName);
        App.WatchForOpenSettingsSignal(openSettingsSignal);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Appends to %LocalAppData%/DynamicIsland/crash.log. Stays on the device.</summary>
    private static void WriteCrashLog(object error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[{DateTimeOffset.Now:u}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing more we can do.
        }
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
