using Avalonia.Data.Converters;

namespace DynamicIsland.UI.Controls;

public static class Converters
{
    /// <summary>true → 1, false → 0; pair with an Opacity transition for a fade.</summary>
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(value => value ? 1 : 0);
}
