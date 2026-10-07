using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.Platform.Windows.SystemStatus;

/// <summary>Combines the Windows volume, power/brightness and privacy watchers.</summary>
internal sealed class WindowsSystemStatusService : ISystemStatusService
{
    private readonly VolumeWatcher _volume = new();
    private readonly PowerWatcher _power = new();
    private readonly PrivacyWatcher _privacy = new();

    public event EventHandler<VolumeLevel>? VolumeChanged;
    public event EventHandler<double>? BrightnessChanged;
    public event EventHandler<PowerStatus>? PowerChanged;
    public event EventHandler? PrivacyChanged;

    public IReadOnlyList<PrivacyUse> PrivacyUses => _privacy.Current;

    public void Start()
    {
        _volume.Changed += (_, level) => VolumeChanged?.Invoke(this, level);
        _power.PowerChanged += (_, status) => PowerChanged?.Invoke(this, status);
        _power.BrightnessChanged += (_, level) => BrightnessChanged?.Invoke(this, level);
        _privacy.Changed += (_, _) => PrivacyChanged?.Invoke(this, EventArgs.Empty);

        // Each part is optional: a failure in one (e.g. no audio device) must not disable the others.
        TryStart(() => ThreadPool.QueueUserWorkItem(_ => TryStart(_volume.Start)));
        TryStart(_power.Start);
        TryStart(_privacy.Start);
    }

    private static void TryStart(Action start)
    {
        try
        {
            start();
        }
        catch (Exception ex)
        {
            // Feature unavailable on this machine.
            DebugLog.Write($"System status: {start.Method.Name} failed: {ex}");
        }
    }

    public void Dispose()
    {
        _volume.Dispose();
        _power.Dispose();
        _privacy.Dispose();
    }
}
