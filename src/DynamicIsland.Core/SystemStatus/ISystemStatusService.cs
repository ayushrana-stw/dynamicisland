namespace DynamicIsland.Core.SystemStatus;

/// <param name="Level">0–1.</param>
public readonly record struct VolumeLevel(double Level, bool IsMuted);

/// <param name="Percent">Battery charge 0–100, or null on a PC without a battery.</param>
public readonly record struct PowerStatus(bool IsOnAc, int? Percent);

/// <summary>An app currently using the microphone and/or camera.</summary>
public sealed record PrivacyUse(string AppName, bool Microphone, bool Camera);

/// <summary>
/// System events the island can show: volume, display brightness, power and mic/camera use.
/// Implementations must be event-driven (no polling) and may raise events on any thread.
/// </summary>
public interface ISystemStatusService : IDisposable
{
    event EventHandler<VolumeLevel>? VolumeChanged;

    /// <summary>Display brightness 0–1 (laptop panels only).</summary>
    event EventHandler<double>? BrightnessChanged;

    event EventHandler<PowerStatus>? PowerChanged;

    event EventHandler? PrivacyChanged;

    IReadOnlyList<PrivacyUse> PrivacyUses { get; }

    void Start();
}

public sealed class NullSystemStatusService : ISystemStatusService
{
    public event EventHandler<VolumeLevel>? VolumeChanged { add { } remove { } }
    public event EventHandler<double>? BrightnessChanged { add { } remove { } }
    public event EventHandler<PowerStatus>? PowerChanged { add { } remove { } }
    public event EventHandler? PrivacyChanged { add { } remove { } }
    public IReadOnlyList<PrivacyUse> PrivacyUses => [];
    public void Start() { }
    public void Dispose() { }
}
