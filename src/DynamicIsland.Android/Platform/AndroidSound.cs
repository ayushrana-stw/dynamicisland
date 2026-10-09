using Android.Content;
using Android.Media;

namespace DynamicIsland.Droid.Platform;

internal static class AndroidSound
{
    /// <summary>The phone's notification sound, for "timer finished".</summary>
    public static void PlayAlert(Context context)
    {
        try
        {
            var uri = RingtoneManager.GetDefaultUri(RingtoneType.Notification);
            RingtoneManager.GetRingtone(context, uri)?.Play();
        }
        catch (Exception)
        {
            // Sound is a nicety; never fail because of it.
        }
    }
}
