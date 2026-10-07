using System.Runtime.InteropServices;

namespace DynamicIsland.App.Platform.Windows;

internal static partial class WindowsSound
{
    private const uint SndAlias = 0x10000, SndAsync = 0x1, SndNoDefault = 0x2;

    /// <summary>Plays the user's "Asterisk" system sound (respects their sound scheme and mute).</summary>
    public static void PlayAsterisk() => PlaySoundW("SystemAsterisk", 0, SndAlias | SndAsync | SndNoDefault);

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string sound, nint module, uint flags);
}
