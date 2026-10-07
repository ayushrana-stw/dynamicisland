using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace DynamicIsland.App.Platform.Windows;

/// <summary>
/// Win32 integration for the island window:
/// a window region for click-through, a global hotkey, and appbar notifications for full-screen apps.
/// </summary>
internal sealed partial class WindowsIslandPlatform : IIslandWindowPlatform
{
    private const int HotkeyId = 0x4449; // "DI"
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_I = 0x49;

    private const uint ABM_NEW = 0x0, ABM_REMOVE = 0x1;
    private const int ABN_FULLSCREENAPP = 0x2;

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TOOLWINDOW = 0x80;

    private nint _hwnd;
    private uint _appBarMessage;
    private bool _hotkeyRegistered;
    private bool _appBarRegistered;

    public event EventHandler? HotkeyPressed;
    public event EventHandler<bool>? FullScreenChanged;

    public string? HotkeyDescription => _hotkeyRegistered ? "Ctrl + Alt + I" : null;

    public void Attach(Window window)
    {
        _hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (_hwnd == 0)
            return;

        Win32Properties.AddWndProcHookCallback(window, WndProc);

        // Tool windows stay out of Alt+Tab and Task View.
        var exStyle = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        _hotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_I);

        // Registering as an appbar (without reserving any screen space) makes the shell
        // notify us when a full-screen app opens or closes, so nothing has to be polled.
        _appBarMessage = RegisterWindowMessageW("DynamicIsland.AppBar");
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uCallbackMessage = _appBarMessage,
        };
        _appBarRegistered = SHAppBarMessage(ABM_NEW, ref data) != 0;
    }

    public void SetInteractiveBounds(PixelRect bounds)
    {
        if (_hwnd == 0)
            return;

        var region = CreateRectRgn(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
        // On success the system owns the region; only free it on failure.
        if (SetWindowRgn(_hwnd, region, true) == 0)
            DeleteObject(region);
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        else if (_appBarMessage != 0 && msg == _appBarMessage && wParam == ABN_FULLSCREENAPP)
        {
            handled = true;
            FullScreenChanged?.Invoke(this, lParam != 0);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_hwnd == 0)
            return;

        if (_hotkeyRegistered)
            UnregisterHotKey(_hwnd, HotkeyId);

        if (_appBarRegistered)
        {
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            SHAppBarMessage(ABM_REMOVE, ref data);
        }

        _hwnd = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    [LibraryImport("shell32.dll")]
    private static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
