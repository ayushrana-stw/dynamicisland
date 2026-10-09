using Android.Runtime;
using Avalonia.Android;

namespace DynamicIsland.Droid;

/// <summary>Starts Avalonia with the process, so the overlay service can show the island without an activity.</summary>
[global::Android.App.Application(Label = "Dynamic Island", Icon = "@drawable/ic_launcher")]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => WriteCrashLog(e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        base.OnCreate();
    }

    /// <summary>Appends to crash.log in the app's private storage. Stays on the device.</summary>
    private void WriteCrashLog(Exception error)
    {
        try
        {
            var path = Path.Combine(FilesDir!.AbsolutePath, "crash.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:u}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing more we can do.
        }
    }
}
