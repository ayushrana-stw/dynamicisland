using Avalonia;
using Avalonia.Controls;

namespace DynamicIsland.UI.Views;

/// <summary>All settings, shared by the desktop settings window and the Android app's main screen.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Optional content shown above the first section, such as the Android permission checklist.</summary>
    public static readonly StyledProperty<object?> TopContentProperty =
        AvaloniaProperty.Register<SettingsView, object?>(nameof(TopContent));

    public SettingsView()
    {
        InitializeComponent();
    }

    public object? TopContent
    {
        get => GetValue(TopContentProperty);
        set => SetValue(TopContentProperty, value);
    }
}
