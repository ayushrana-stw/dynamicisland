using DynamicIsland.Core.Platform;
using Microsoft.Win32;

namespace DynamicIsland.App.Platform.Windows;

/// <summary>Per-user "run at sign-in" through the HKCU Run key. No admin rights needed.</summary>
internal sealed class WindowsStartupManager : IStartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    public bool IsSupported => Environment.ProcessPath is not null;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
