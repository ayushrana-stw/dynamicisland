using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.App.Platform.Windows.Teams;

/// <summary>
/// New Teams shows its own pop-ups instead of Windows notifications, so the notification
/// listener never sees them. This watches for Teams' pop-up window to appear (a Windows
/// event hook, no polling) and reads its text through UI Automation, the accessibility
/// interface screen readers use. Everything stays on this PC.
/// </summary>
internal sealed partial class TeamsPopupWatcher : IDisposable
{
    public const string TeamsAppId = "MSTeams_8wekyb3d8bbwe!MSTeams";

    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000, WineventSkipOwnProcess = 0x0002;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int TreeScopeDescendants = 4;

    private static readonly Guid CUIAutomationClsid = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IUIAutomationIid = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    private static TeamsPopupWatcher? s_instance;

    private readonly StrategyBasedComWrappers _comWrappers = new();
    private readonly Dictionary<string, DateTime> _recent = [];
    private nint _hook;
    private uint _nextId = 0xF0000000; // Well away from Windows' own notification ids.
    private byte[]? _teamsIcon;
    private bool _iconLoaded;

    public event EventHandler<IslandNotification>? Received;

    /// <summary>Must be called on a thread with a message loop (the UI thread).</summary>
    public unsafe void Start()
    {
        if (_hook != 0)
            return;

        s_instance = this;
        _hook = SetWinEventHook(EventObjectShow, EventObjectShow, 0, &OnWinEvent, 0, 0,
            WineventOutOfContext | WineventSkipOwnProcess);
        DebugLog.Write($"Teams: pop-up watcher {(_hook != 0 ? "started" : "failed to start")}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Only whole windows, not the countless controls inside them.
        if (hwnd == 0 || idObject != 0 || idChild != 0)
            return;

        try
        {
            s_instance?.Consider(hwnd);
        }
        catch (Exception)
        {
            // Never let an exception cross back into Windows.
        }
    }

    /// <summary>Cheap checks on the UI thread; the slow accessibility read happens on a worker.</summary>
    private void Consider(nint hwnd)
    {
        if (GetAncestor(hwnd, 2 /* GA_ROOT */) != hwnd)
            return;

        var className = new StringBuilder(64);
        GetClassNameW(hwnd, className, className.Capacity);
        if (className.ToString() != "TeamsWebView")
            return;

        // Teams' main windows are normal windows; its pop-ups are always-on-top tool windows.
        if ((GetWindowLongW(hwnd, GwlExStyle) & WsExToolWindow) == 0)
            return;

        ThreadPool.QueueUserWorkItem(_ => _ = ReadPopupAsync(hwnd));
    }

    private async Task ReadPopupAsync(nint hwnd)
    {
        // The pop-up's web content renders a moment after the window appears.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 400 : 300));
            if (!IsWindow(hwnd))
                return;

            var elements = ReadElements(hwnd);
            if (Parse(elements) is not { } parsed)
                continue;

            if (!IsNew(parsed.Title, parsed.Body))
                return;

            DebugLog.Write("Teams: read a pop-up");
            Received?.Invoke(this, new IslandNotification(
                Interlocked.Increment(ref _nextId), TeamsAppId, "Teams", parsed.Title, parsed.Body,
                TeamsIcon(), DateTimeOffset.Now)
            {
                FromAppWindow = true,
            });
            return;
        }

        DebugLog.Write("Teams: pop-up appeared but had no readable text");
    }

    /// <summary>Lists (control type, name) for every element in the pop-up.</summary>
    private List<(int Type, string Name)> ReadElements(nint hwnd)
    {
        var result = new List<(int, string)>();
        try
        {
            if (CoCreateInstance(CUIAutomationClsid, 0, 1 /* CLSCTX_INPROC_SERVER */, IUIAutomationIid, out var pointer) < 0)
                return result;

            var automation = (IUIAutomation)_comWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            Marshal.Release(pointer);

            if (automation.ElementFromHandle(hwnd, out var root) < 0 || root is null
                || automation.CreateTrueCondition(out var condition) < 0 || condition is null
                || root.FindAll(TreeScopeDescendants, condition, out var found) < 0 || found is null
                || found.get_Length(out var length) < 0)
                return result;

            for (var i = 0; i < Math.Min(length, 80); i++)
            {
                if (found.GetElement(i, out var element) < 0 || element is null)
                    continue;

                element.get_CurrentControlType(out var type);
                element.get_CurrentName(out var name);
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add((type, name.Trim()));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Teams: accessibility read failed: {ex.GetType().Name}");
        }

        return result;
    }

    /// <summary>
    /// A chat pop-up reads: …, [Button] Dismiss notification, [Text] chat or person, [Text] message, [Edit] reply.
    /// Other kinds (calls, mentions) fall back to the first two plain texts.
    /// </summary>
    private static (string Title, string Body)? Parse(List<(int Type, string Name)> elements)
    {
        var dismiss = elements.FindIndex(e => e.Type == UiaControlType.Button && e.Name.Contains("Dismiss", StringComparison.OrdinalIgnoreCase));
        if (dismiss >= 0)
        {
            var texts = elements.Skip(dismiss + 1)
                .TakeWhile(e => e.Type != UiaControlType.Edit)
                .Where(e => e.Type == UiaControlType.Text)
                .Select(e => e.Name)
                .Distinct()
                .ToList();

            if (texts.Count >= 2)
                return (texts[0], string.Join(Environment.NewLine, texts.Skip(1)));
            if (texts.Count == 1)
                return ("Teams", texts[0]);
        }

        // Fallback: skip the window's own title and account labels (which contain the email address).
        var plain = elements
            .Where(e => e.Type == UiaControlType.Text && !e.Name.Contains('@') && e.Name != "Microsoft Teams")
            .Select(e => e.Name)
            .Distinct()
            .ToList();

        return plain.Count switch
        {
            0 => null,
            1 => ("Teams", plain[0]),
            _ => (plain[^2], plain[^1]),
        };
    }

    /// <summary>Teams re-shows the same pop-up window; only report each message once.</summary>
    private bool IsNew(string title, string body)
    {
        var key = title + "\u001f" + body;
        var now = DateTime.UtcNow;
        lock (_recent)
        {
            foreach (var stale in _recent.Where(r => now - r.Value > TimeSpan.FromSeconds(30)).Select(r => r.Key).ToList())
                _recent.Remove(stale);

            if (_recent.ContainsKey(key))
                return false;

            _recent[key] = now;
            return true;
        }
    }

    /// <summary>The Teams logo from its installed package, read once.</summary>
    private byte[]? TeamsIcon()
    {
        if (_iconLoaded)
            return _teamsIcon;
        _iconLoaded = true;

        try
        {
            var process = System.Diagnostics.Process.GetProcessesByName("ms-teams").FirstOrDefault();
            if (process is null)
                return null;

            var path = new StringBuilder(1024);
            var size = path.Capacity;
            var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, process.Id);
            if (handle == 0)
                return null;

            var ok = QueryFullProcessImageNameW(handle, 0, path, ref size);
            CloseHandle(handle);
            if (!ok || Path.GetDirectoryName(path.ToString()) is not { } folder)
                return null;

            var logo = Directory.EnumerateFiles(folder, "*44x44*targetsize-64*.png", SearchOption.AllDirectories).FirstOrDefault()
                       ?? Directory.EnumerateFiles(folder, "*44x44*.png", SearchOption.AllDirectories).FirstOrDefault();
            _teamsIcon = logo is null ? null : File.ReadAllBytes(logo);
        }
        catch (Exception)
        {
            // Packaged app folders can be locked down; the island shows a bell instead.
        }

        return _teamsIcon;
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }

        if (s_instance == this)
            s_instance = null;
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder className, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
