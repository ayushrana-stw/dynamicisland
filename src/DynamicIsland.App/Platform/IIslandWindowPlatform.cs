using Avalonia;
using Avalonia.Controls;

namespace DynamicIsland.App.Platform;

/// <summary>OS-specific behaviour of the island window.</summary>
internal interface IIslandWindowPlatform : IDisposable
{
    /// <summary>Raised when the global "open island" shortcut is pressed.</summary>
    event EventHandler? HotkeyPressed;

    /// <summary>Raised with true when a full-screen app takes over the island's display, false when it leaves.</summary>
    event EventHandler<bool>? FullScreenChanged;

    /// <summary>Human-readable shortcut shown in the UI, or null if none is registered.</summary>
    string? HotkeyDescription { get; }

    void Attach(Window window);

    /// <summary>Limits mouse input to <paramref name="bounds"/> (physical pixels, window-relative) so clicks elsewhere reach the apps below.</summary>
    void SetInteractiveBounds(PixelRect bounds);
}

internal sealed class NullIslandWindowPlatform : IIslandWindowPlatform
{
    public event EventHandler? HotkeyPressed { add { } remove { } }
    public event EventHandler<bool>? FullScreenChanged { add { } remove { } }
    public string? HotkeyDescription => null;
    public void Attach(Window window) { }
    public void SetInteractiveBounds(PixelRect bounds) { }
    public void Dispose() { }
}
