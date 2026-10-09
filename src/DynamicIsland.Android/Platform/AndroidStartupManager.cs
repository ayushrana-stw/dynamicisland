using DynamicIsland.Core.Platform;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Droid.Platform;

/// <summary>
/// "Start when the phone starts" is just the saved setting: <see cref="BootReceiver"/> reads it at boot.
/// </summary>
internal sealed class AndroidStartupManager(Func<SettingsService> settings) : IStartupManager
{
    public bool IsSupported => true;

    public bool IsEnabled => settings().Current.StartWithSystem;

    // The settings screen saves StartWithSystem itself; nothing else to register on Android.
    public void SetEnabled(bool enabled)
    {
    }
}
