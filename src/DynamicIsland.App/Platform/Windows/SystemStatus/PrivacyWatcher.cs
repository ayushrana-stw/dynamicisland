using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.SystemStatus;
using Microsoft.Win32;

namespace DynamicIsland.App.Platform.Windows.SystemStatus;

/// <summary>
/// Detects apps using the microphone or camera from Windows' own privacy records
/// (the same data behind the taskbar's microphone icon). A registry change notification
/// wakes a dedicated thread; nothing polls.
/// </summary>
internal sealed partial class PrivacyWatcher : IDisposable
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private const uint RegNotifyChangeName = 0x1, RegNotifyChangeLastSet = 0x4;

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MSTeams"] = "Teams",
        ["MicrosoftTeams"] = "Teams",
        ["Microsoft.WindowsCamera"] = "Camera",
        ["Microsoft.WindowsSoundRecorder"] = "Sound Recorder",
        ["5319275A.WhatsAppDesktop"] = "WhatsApp",
        ["Microsoft.SkypeApp"] = "Skype",
        ["Microsoft.ScreenSketch"] = "Snipping Tool",
    };

    private readonly ManualResetEvent _stop = new(false);
    private readonly Dictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private Thread? _thread;

    public event EventHandler? Changed;

    public IReadOnlyList<PrivacyUse> Current { get; private set; } = [];

    public void Start()
    {
        Current = Scan();
        _thread = new Thread(Watch) { IsBackground = true, Name = "Privacy watcher" };
        _thread.Start();
    }

    private void Watch()
    {
        using var microphone = Registry.CurrentUser.OpenSubKey(ConsentStore + "microphone");
        using var webcam = Registry.CurrentUser.OpenSubKey(ConsentStore + "webcam");
        using var microphoneChanged = new AutoResetEvent(false);
        using var webcamChanged = new AutoResetEvent(false);

        while (true)
        {
            Arm(microphone, microphoneChanged);
            Arm(webcam, webcamChanged);

            if (WaitHandle.WaitAny([microphoneChanged, webcamChanged, _stop]) == 2)
                return;

            // Apps write several values at once; let them settle.
            if (_stop.WaitOne(TimeSpan.FromMilliseconds(150)))
                return;

            var scan = Scan();
            if (!scan.SequenceEqual(Current))
            {
                Current = scan;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static void Arm(RegistryKey? key, AutoResetEvent signal)
    {
        if (key is not null)
            RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), true, RegNotifyChangeName | RegNotifyChangeLastSet,
                signal.SafeWaitHandle.DangerousGetHandle(), true);
    }

    private List<PrivacyUse> Scan()
    {
        var microphone = AppsInUse("microphone");
        var camera = AppsInUse("webcam");

        return microphone.Union(camera, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(app => new PrivacyUse(app, microphone.Contains(app), camera.Contains(app)))
            .ToList();
    }

    private HashSet<string> AppsInUse(string capability)
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(ConsentStore + capability);
        if (root is null)
            return apps;

        foreach (var name in root.GetSubKeyNames())
        {
            if (name == "NonPackaged")
            {
                using var nonPackaged = root.OpenSubKey(name);
                foreach (var exe in nonPackaged?.GetSubKeyNames() ?? [])
                {
                    using var key = nonPackaged!.OpenSubKey(exe);
                    if (IsInUse(key))
                        apps.Add(DesktopAppName(exe));
                }
            }
            else
            {
                using var key = root.OpenSubKey(name);
                if (IsInUse(key))
                    apps.Add(PackagedAppName(name));
            }
        }

        return apps;
    }

    /// <summary>An app is using the device while it has a start time but no stop time.</summary>
    private static bool IsInUse(RegistryKey? key) =>
        key?.GetValue("LastUsedTimeStop") is long stop && stop == 0
        && key.GetValue("LastUsedTimeStart") is long start && start != 0;

    /// <summary>"MSTeams_8wekyb3d8bbwe" → "Teams".</summary>
    private static string PackagedAppName(string familyName)
    {
        var name = familyName.Split('_')[0];
        if (KnownApps.TryGetValue(name, out var known))
            return known;

        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    /// <summary>"C:#Program Files#Zoom#bin#Zoom.exe" → the exe's product description, e.g. "Zoom Meetings".</summary>
    private string DesktopAppName(string encodedPath)
    {
        if (_nameCache.TryGetValue(encodedPath, out var cached))
            return cached;

        var path = encodedPath.Replace('#', '\\');
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                name = description;
        }
        catch (Exception)
        {
            // Keep the file name.
        }

        _nameCache[encodedPath] = name;
        return name;
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(nint key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter, nint eventHandle, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
