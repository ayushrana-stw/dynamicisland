using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.App.Platform.Windows.SystemStatus;

/// <summary>
/// Power source, battery level and display brightness through power-setting notifications.
/// Windows calls back only when a value changes; no window or polling is needed.
/// </summary>
internal sealed unsafe partial class PowerWatcher : IDisposable
{
    private static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly Guid BatteryPercentage = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    private static readonly Guid MonitorBrightness = new("8FFEE2C6-2D01-46BE-ADB9-398ADDC5B4FF");

    private const uint DeviceNotifyCallback = 2;
    private const uint PbtPowerSettingChange = 0x8013;

    private readonly List<nint> _registrations = [];
    private GCHandle _self;
    private DeviceNotifySubscribeParameters* _parameters;

    private bool? _isOnAc;
    private int? _batteryPercent;
    private bool _brightnessKnown;

    public event EventHandler<PowerStatus>? PowerChanged;
    public event EventHandler<double>? BrightnessChanged;

    public bool HasBattery { get; private set; }

    public void Start()
    {
        HasBattery = GetSystemPowerStatus(out var status) && status.BatteryFlag is not 128 and not 255;

        _self = GCHandle.Alloc(this);
        _parameters = (DeviceNotifySubscribeParameters*)NativeMemory.Alloc((nuint)sizeof(DeviceNotifySubscribeParameters));
        _parameters->Callback = &OnPowerSetting;
        _parameters->Context = GCHandle.ToIntPtr(_self);

        foreach (var setting in new[] { AcDcPowerSource, BatteryPercentage, MonitorBrightness })
        {
            if (PowerSettingRegisterNotification(setting, DeviceNotifyCallback, (nint)_parameters, out var handle) == 0)
                _registrations.Add(handle);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnPowerSetting(nint context, uint type, nint setting)
    {
        if (type != PbtPowerSettingChange || setting == 0)
            return 0;

        try
        {
            if (GCHandle.FromIntPtr(context).Target is PowerWatcher watcher)
            {
                // POWERBROADCAST_SETTING: GUID (16), DWORD length (4), data...
                var guid = *(Guid*)setting;
                var value = *(uint*)(setting + 20);
                watcher.Handle(guid, value);
            }
        }
        catch (Exception)
        {
            // Never let an exception cross back into Windows.
        }

        return 0;
    }

    private void Handle(Guid setting, uint value)
    {
        if (setting == AcDcPowerSource)
        {
            _isOnAc = value == 0;
            RaisePower();
        }
        else if (setting == BatteryPercentage && HasBattery)
        {
            _batteryPercent = (int)Math.Min(value, 100);
            RaisePower();
        }
        else if (setting == MonitorBrightness)
        {
            // The first callback reports the current value at registration; only real changes are shown.
            if (_brightnessKnown)
                BrightnessChanged?.Invoke(this, Math.Clamp(value / 100.0, 0, 1));
            _brightnessKnown = true;
        }
    }

    private void RaisePower()
    {
        if (_isOnAc is { } onAc)
            PowerChanged?.Invoke(this, new PowerStatus(onAc, HasBattery ? _batteryPercent : null));
    }

    public void Dispose()
    {
        foreach (var handle in _registrations)
            PowerSettingUnregisterNotification(handle);
        _registrations.Clear();

        if (_parameters is not null)
        {
            NativeMemory.Free(_parameters);
            _parameters = null;
        }

        if (_self.IsAllocated)
            _self.Free();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public delegate* unmanaged[Stdcall]<nint, uint, nint, uint> Callback;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(in Guid settingGuid, uint flags, nint recipient, out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(nint registrationHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
