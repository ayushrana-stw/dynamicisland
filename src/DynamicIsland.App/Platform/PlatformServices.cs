using DynamicIsland.Core.Media;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Platform;

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
