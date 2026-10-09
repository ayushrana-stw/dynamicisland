using System.Collections.Concurrent;
using Android.Content;
using Android.Content.PM;

namespace DynamicIsland.Droid.Platform;

/// <summary>Display names and icons of other apps, cached because notifications repeat apps a lot.</summary>
internal static class AppInfo
{
    private const int IconSize = 96;

    private static readonly ConcurrentDictionary<string, string> Labels = new();
    private static readonly ConcurrentDictionary<string, byte[]?> Icons = new();

    public static string Label(Context context, string packageName) => Labels.GetOrAdd(packageName, package =>
    {
        try
        {
            var pm = context.PackageManager!;
            var info = pm.GetApplicationInfo(package, PackageInfoFlags.MetaData);
            return pm.GetApplicationLabel(info)?.ToString() is { Length: > 0 } label ? label : package;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return package;
        }
    });

    public static byte[]? IconPng(Context context, string packageName) => Icons.GetOrAdd(packageName, package =>
    {
        try
        {
            return Images.ToPng(context.PackageManager!.GetApplicationIcon(package), IconSize);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    });
}
