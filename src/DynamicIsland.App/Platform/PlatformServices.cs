using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Platform;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.Platform;

/// <summary>Picks the implementation of each platform service for the current OS.</summary>
internal static class PlatformServices
{
    public static IMediaService CreateMediaService()
    {
#if WINDOWS
        return new Windows.WindowsMediaService();
#else
        // macOS: MediaRemote adapter / AppleScript helper — coming in the Mac phase.
        return new NullMediaService();
#endif
    }

    public static INotificationService CreateNotificationService()
    {
#if WINDOWS
        return new Windows.WindowsNotificationService();
#else
        // macOS: Accessibility-based reader — coming in the Mac phase.
        return new NullNotificationService();
#endif
    }

    public static ISystemStatusService CreateSystemStatusService()
    {
#if WINDOWS
        return new Windows.SystemStatus.WindowsSystemStatusService();
#else
        return new NullSystemStatusService();
#endif
    }

    /// <summary>A short, gentle alert sound (timer finished).</summary>
    public static void PlayAlert()
    {
        try
        {
#if WINDOWS
            Windows.WindowsSound.PlayAsterisk();
#else
            if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("afplay", "/System/Library/Sounds/Glass.aiff");
#endif
        }
        catch (Exception)
        {
            // Sound is a nicety; never fail because of it.
        }
    }

    public static IStartupManager CreateStartupManager()
    {
#if WINDOWS
        return new Windows.WindowsStartupManager();
#else
        return OperatingSystem.IsMacOS() ? new Mac.MacStartupManager() : new NullStartupManager();
#endif
    }

    public static IIslandWindowPlatform CreateIslandWindowPlatform()
    {
#if WINDOWS
        return new Windows.WindowsIslandPlatform();
#else
        return new NullIslandWindowPlatform();
#endif
    }
}
