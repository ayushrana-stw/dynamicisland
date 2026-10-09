using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DynamicIsland.UI.ViewModels;

namespace DynamicIsland.UI.Views;

/// <summary>
/// The island and its side bubble, with the input handling every host shares.
/// Hosts (the desktop window, the Android overlay) place it and decide what clicks outside it do.
/// </summary>
public partial class IslandView : UserControl
{
    private IslandViewModel? _viewModel;

    public IslandView()
    {
        InitializeComponent();

        Island.Tapped += OnIslandTapped;
        Island.PointerEntered += (_, e) => { if (IsHoverPointer(e)) _viewModel?.OnPointerEntered(); };
        Island.PointerExited += (_, e) => { if (IsHoverPointer(e)) _viewModel?.OnPointerExited(); };
        Island.PointerPressed += OnIslandPointerPressed;
        Island.PointerReleased += (_, _) => Island.Classes.Remove("pressing");
        Island.PointerCaptureLost += (_, _) => Island.Classes.Remove("pressing");

        // The side bubble opens what it represents: the timer, or the island (mic/camera details).
        Bubble.Tapped += (_, _) =>
        {
            if (_viewModel is { BubbleShowsTimer: true })
                _viewModel.OpenTimerPickerCommand.Execute(null);
            else
                _viewModel?.Expand(peek: false);
        };
    }

    /// <summary>The island's own surface (not the bubble), for focus and pointer checks.</summary>
    public Border IslandSurface => Island;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = DataContext as IslandViewModel;
    }

    /// <summary>A finger has no hover: touching the island should tap it, not preview it.</summary>
    private static bool IsHoverPointer(PointerEventArgs e) => e.Pointer.Type != PointerType.Touch;

    private void OnIslandPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        Island.Classes.Add("pressing");
    }

    private void OnIslandTapped(object? sender, TappedEventArgs e)
    {
        // Taps on the island's own buttons are handled by those buttons.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        // Tapping a notification opens the app that sent it, like a banner on a phone.
        if (_viewModel is { ShowExpandedNotification: true })
            _viewModel.OpenNotificationCommand.Execute(null);
        else
            _viewModel?.ToggleExpanded();
    }
}
