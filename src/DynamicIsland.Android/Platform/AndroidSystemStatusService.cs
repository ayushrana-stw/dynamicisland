using Android.Content;
using Android.Database;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using DynamicIsland.Core.SystemStatus;
using AndroidSettings = Android.Provider.Settings;
using Stream = Android.Media.Stream;

namespace DynamicIsland.Droid.Platform;

/// <summary>
/// Volume, screen brightness, battery and microphone/camera use, all from system callbacks (no polling).
/// Android doesn't say which other app uses the microphone or camera, so the island says "An app".
/// </summary>
internal sealed class AndroidSystemStatusService : ISystemStatusService
{
    private const string VolumeChangedAction = "android.media.VOLUME_CHANGED_ACTION";
    private const string VolumeStreamTypeExtra = "android.media.EXTRA_VOLUME_STREAM_TYPE";

    private readonly Context _context;
    private readonly Handler _mainThread = new(Looper.MainLooper!);
    private readonly AudioManager _audio;
    private readonly CameraManager _cameras;
    private readonly Receiver _volumeReceiver;
    private readonly Receiver _batteryReceiver;
    private readonly BrightnessObserver _brightnessObserver;
    private readonly RecordingCallback _recordingCallback;
    private readonly CameraCallback _cameraCallback;
    private readonly HashSet<string> _camerasInUse = [];

    private PowerStatus? _lastPower;
    private int _lastBrightness = -1;
    private bool _micInUse;
    private bool _started;

    public AndroidSystemStatusService(Context context)
    {
        _context = context;
        _audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        _cameras = (CameraManager)context.GetSystemService(Context.CameraService)!;
        _volumeReceiver = new Receiver(OnVolumeBroadcast);
        _batteryReceiver = new Receiver(OnBatteryBroadcast);
        _brightnessObserver = new BrightnessObserver(_mainThread, OnBrightnessSettingChanged);
        _recordingCallback = new RecordingCallback(OnRecordingChanged);
        _cameraCallback = new CameraCallback(OnCameraChanged);
    }

    public event EventHandler<VolumeLevel>? VolumeChanged;
    public event EventHandler<double>? BrightnessChanged;
    public event EventHandler<PowerStatus>? PowerChanged;
    public event EventHandler? PrivacyChanged;

    public IReadOnlyList<PrivacyUse> PrivacyUses { get; private set; } = [];

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        Register(_volumeReceiver, VolumeChangedAction);

        // The battery broadcast is sticky: registering returns the current state straight away,
        // which gives the island its baseline for "just plugged in" and "dropped below 20%".
        Register(_batteryReceiver, Intent.ActionBatteryChanged);

        _lastBrightness = ReadBrightness();
        _context.ContentResolver!.RegisterContentObserver(
            AndroidSettings.System.GetUriFor(AndroidSettings.System.ScreenBrightness)!, false, _brightnessObserver);

        _audio.RegisterAudioRecordingCallback(_recordingCallback, _mainThread);
        _micInUse = _audio.ActiveRecordingConfigurations.Count > 0;
        _cameras.RegisterAvailabilityCallback(_cameraCallback, _mainThread);
        UpdatePrivacy();
    }

    private void Register(BroadcastReceiver receiver, string action)
    {
        var filter = new IntentFilter(action);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            _context.RegisterReceiver(receiver, filter);
    }

    // ---- Volume ------------------------------------------------------------------------------

    private void OnVolumeBroadcast(Intent intent)
    {
        // Only media volume; ring and alarm changes would be noise.
        if (intent.GetIntExtra(VolumeStreamTypeExtra, -1) != (int)Stream.Music)
            return;

        var max = _audio.GetStreamMaxVolume(Stream.Music);
        var level = max > 0 ? (double)_audio.GetStreamVolume(Stream.Music) / max : 0;
        VolumeChanged?.Invoke(this, new VolumeLevel(level, _audio.IsStreamMute(Stream.Music)));
    }

    // ---- Brightness ----------------------------------------------------------------------------

    private int ReadBrightness() =>
        AndroidSettings.System.GetInt(_context.ContentResolver, AndroidSettings.System.ScreenBrightness, -1);

    private void OnBrightnessSettingChanged()
    {
        var value = ReadBrightness();
        if (value < 0 || value == _lastBrightness)
            return;

        _lastBrightness = value;

        // The setting is 0–255 on almost every phone (a few use a larger range; clamp those).
        BrightnessChanged?.Invoke(this, Math.Clamp(value / 255.0, 0, 1));
    }

    // ---- Battery ---------------------------------------------------------------------------------

    private void OnBatteryBroadcast(Intent intent)
    {
        var level = intent.GetIntExtra(BatteryManager.ExtraLevel, -1);
        var scale = intent.GetIntExtra(BatteryManager.ExtraScale, 100);
        var plugged = intent.GetIntExtra(BatteryManager.ExtraPlugged, 0) != 0;
        int? percent = level >= 0 && scale > 0 ? (int)Math.Round(level * 100.0 / scale) : null;

        // The broadcast also fires for temperature and voltage changes; only report what the island shows.
        var status = new PowerStatus(plugged, percent);
        if (status == _lastPower)
            return;

        _lastPower = status;
        PowerChanged?.Invoke(this, status);
    }

    // ---- Microphone / camera ---------------------------------------------------------------------

    private void OnRecordingChanged(int activeRecordings)
    {
        _micInUse = activeRecordings > 0;
        UpdatePrivacy();
    }

    private void OnCameraChanged(string cameraId, bool inUse)
    {
        if (inUse)
            _camerasInUse.Add(cameraId);
        else
            _camerasInUse.Remove(cameraId);
        UpdatePrivacy();
    }

    private void UpdatePrivacy()
    {
        var camera = _camerasInUse.Count > 0;
        IReadOnlyList<PrivacyUse> uses = _micInUse || camera ? [new PrivacyUse("An app", _micInUse, camera)] : [];

        var previous = PrivacyUses;
        PrivacyUses = uses;
        if (previous.Count != uses.Count || (uses.Count > 0 && previous[0] != uses[0]))
            PrivacyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (!_started)
            return;

        _context.UnregisterReceiver(_volumeReceiver);
        _context.UnregisterReceiver(_batteryReceiver);
        _context.ContentResolver!.UnregisterContentObserver(_brightnessObserver);
        _audio.UnregisterAudioRecordingCallback(_recordingCallback);
        _cameras.UnregisterAvailabilityCallback(_cameraCallback);
        _started = false;
    }

    private sealed class Receiver(Action<Intent> received) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is not null)
                received(intent);
        }
    }

    private sealed class BrightnessObserver(Handler handler, Action changed) : ContentObserver(handler)
    {
        public override void OnChange(bool selfChange) => changed();
    }

    private sealed class RecordingCallback(Action<int> changed) : AudioManager.AudioRecordingCallback
    {
        public override void OnRecordingConfigChanged(IList<AudioRecordingConfiguration>? configs) => changed(configs?.Count ?? 0);
    }

    private sealed class CameraCallback(Action<string, bool> changed) : CameraManager.AvailabilityCallback
    {
        public override void OnCameraUnavailable(string cameraId) => changed(cameraId, true);
        public override void OnCameraAvailable(string cameraId) => changed(cameraId, false);
    }
}
