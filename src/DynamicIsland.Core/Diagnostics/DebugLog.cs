namespace DynamicIsland.Core.Diagnostics;

/// <summary>
/// Opt-in troubleshooting log: set the environment variable <c>DYNAMIC_ISLAND_DEBUG=1</c> and
/// messages go to <c>debug.log</c> next to the settings file. Off by default; never leaves the device.
/// </summary>
public static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly string? Path = Environment.GetEnvironmentVariable("DYNAMIC_ISLAND_DEBUG") == "1"
        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "debug.log")
        : null;

    public static void Write(string message)
    {
        if (Path is null)
            return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never break the app.
        }
    }
}
