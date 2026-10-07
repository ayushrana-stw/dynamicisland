using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.Platform.Windows.SystemStatus;

/// <summary>Raises <see cref="Changed"/> when the default speaker's volume or mute state changes.</summary>
internal sealed partial class VolumeWatcher : IDisposable
{
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IMMDeviceEnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IAudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const uint ClsctxAll = 0x17;
    private const int Render = 0, Multimedia = 1;

    private readonly StrategyBasedComWrappers _comWrappers = new();
    private readonly object _gate = new();
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceCallback _deviceCallback;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;

    public VolumeWatcher()
    {
        _volumeCallback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public event EventHandler<VolumeLevel>? Changed;

    /// <summary>Call from a thread-pool (MTA) thread.</summary>
    public void Start()
    {
        var hr = CoCreateInstance(MMDeviceEnumeratorClsid, 0, ClsctxAll, IMMDeviceEnumeratorIid, out var pointer);
        DebugLog.Write($"Volume: CoCreateInstance hr=0x{hr:X8}");
        if (hr < 0)
            return;

        _enumerator = (IMMDeviceEnumerator)_comWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        Marshal.Release(pointer);

        _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
        BindDefaultEndpoint();
    }

    /// <summary>Follows the default output device (e.g. switching from speakers to headphones).</summary>
    private void BindDefaultEndpoint()
    {
        lock (_gate)
        {
            if (_endpoint is not null)
            {
                _endpoint.UnregisterControlChangeNotify(_volumeCallback);
                _endpoint = null;
            }

            if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out var device) < 0 || device is null)
                return;

            if (device.Activate(IAudioEndpointVolumeIid, ClsctxAll, 0, out var pointer) < 0)
                return;

            _endpoint = (IAudioEndpointVolume)_comWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            Marshal.Release(pointer);
            var hr = _endpoint.RegisterControlChangeNotify(_volumeCallback);
            DebugLog.Write($"Volume: bound to default endpoint, register hr=0x{hr:X8}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _endpoint?.UnregisterControlChangeNotify(_volumeCallback);
            _enumerator?.UnregisterEndpointNotificationCallback(_deviceCallback);
            _endpoint = null;
            _enumerator = null;
        }
    }

    [GeneratedComClass]
    private sealed partial class VolumeCallback(VolumeWatcher owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID context (16), BOOL muted (4), float master volume (4), ...
            var muted = Marshal.ReadInt32(data, 16) != 0;
            var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            DebugLog.Write($"Volume: notify level={level:0.00} muted={muted}");
            owner.Changed?.Invoke(owner, new VolumeLevel(Math.Clamp(level, 0, 1), muted));
            return 0;
        }
    }

    [GeneratedComClass]
    private sealed partial class DeviceCallback(VolumeWatcher owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            // Never call back into Core Audio from inside its own callback.
            if (flow == Render && role == Multimedia)
                ThreadPool.QueueUserWorkItem(_ => owner.BindDefaultEndpoint());
            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);
}
