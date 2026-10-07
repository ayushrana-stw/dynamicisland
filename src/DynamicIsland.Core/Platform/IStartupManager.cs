namespace DynamicIsland.Core.Platform;

/// <summary>Controls whether the app launches when the user signs in.</summary>
public interface IStartupManager
{
    bool IsSupported { get; }
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class NullStartupManager : IStartupManager
{
    public bool IsSupported => false;
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
}
