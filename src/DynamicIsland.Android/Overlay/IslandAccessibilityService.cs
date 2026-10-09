using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Views;
using Android.Views.Accessibility;

namespace DynamicIsland.Droid.Overlay;

/// <summary>
/// The preferred host: Android lets accessibility services draw above the status bar, so the island can sit
/// around the camera and still be tapped, and no "running" notification is needed. It reads no screen content.
/// </summary>
[Service(
    Name = "com.dynamicisland.app.IslandAccessibilityService",
    Label = "Dynamic Island",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE",
    Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service")]
public sealed class IslandAccessibilityService : AccessibilityService
{
    private IslandHost? _island;

    /// <summary>The connected service, or null while the user has it turned off.</summary>
    public static IslandAccessibilityService? Instance { get; private set; }

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        Instance = this;
        IslandRuntime.SyncIsland(this);
    }

    /// <summary>Shows or removes the island; called by <see cref="IslandRuntime.SyncIsland"/>.</summary>
    internal void SetIslandVisible(bool visible)
    {
        if (visible && _island is null)
            _island = new IslandHost(this, WindowManagerTypes.AccessibilityOverlay);
        else if (!visible && _island is not null)
        {
            _island.Dispose();
            _island = null;
        }
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _island?.Reposition();
    }

    // The island doesn't use accessibility events; it only needs the overlay window type.
    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
    }

    public override void OnInterrupt()
    {
    }

    public override bool OnUnbind(Intent? intent)
    {
        Disconnect();
        return base.OnUnbind(intent);
    }

    public override void OnDestroy()
    {
        Disconnect();
        base.OnDestroy();
    }

    private void Disconnect()
    {
        if (Instance != this)
            return;

        SetIslandVisible(false);
        Instance = null;

        // Fall back to the foreground service if the island should still show.
        IslandRuntime.SyncIsland(ApplicationContext!);
    }
}
