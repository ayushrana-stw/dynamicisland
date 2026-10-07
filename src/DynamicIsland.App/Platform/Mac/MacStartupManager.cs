using System.Security;
using DynamicIsland.Core.Platform;

namespace DynamicIsland.App.Platform.Mac;

/// <summary>Per-user "run at login" through a LaunchAgent plist.</summary>
internal sealed class MacStartupManager : IStartupManager
{
    private const string Label = "com.dynamicisland.app";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", Label + ".plist");

    public bool IsSupported => Environment.ProcessPath is not null;

    public bool IsEnabled => File.Exists(PlistPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }

        if (Environment.ProcessPath is not { } exe)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key><array><string>{SecurityElement.Escape(exe)}</string></array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """);
    }
}
