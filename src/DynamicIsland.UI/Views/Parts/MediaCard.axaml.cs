using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicIsland.UI.ViewModels;

namespace DynamicIsland.UI.Views.Parts;

public partial class MediaCard : UserControl
{
    private IslandViewModel? _viewModel;

    public MediaCard()
    {
        InitializeComponent();

        SeekArea.PointerPressed += OnSeekPressed;
        SeekArea.PointerMoved += OnSeekMoved;
        SeekArea.PointerReleased += OnSeekReleased;
        SeekArea.PointerCaptureLost += (_, _) => _viewModel?.EndScrub(_lastFraction);
    }

    private double _lastFraction;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.TrackChanged -= OnTrackChanged;

        _viewModel = DataContext as IslandViewModel;

        if (_viewModel is not null)
            _viewModel.TrackChanged += OnTrackChanged;
    }

    /// <summary>The artwork pops briefly; its spring transition carries it back.</summary>
    private void OnTrackChanged(object? sender, EventArgs e)
    {
        Art.Classes.Add("bump");
        DispatcherTimer.RunOnce(() => Art.Classes.Remove("bump"), TimeSpan.FromMilliseconds(170));
    }

    // ---- Drag to seek ------------------------------------------------------------------------

    private double FractionAt(PointerEventArgs e) =>
        SeekArea.Bounds.Width > 0 ? Math.Clamp(e.GetPosition(SeekArea).X / SeekArea.Bounds.Width, 0, 1) : 0;

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not { CanSeek: true })
            return;

        _lastFraction = FractionAt(e);
        e.Pointer.Capture(SeekArea);
        _viewModel.BeginScrub(_lastFraction);
        e.Handled = true; // Don't let the island treat this as a tap.
    }

    private void OnSeekMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is not { IsScrubbing: true })
            return;

        _lastFraction = FractionAt(e);
        _viewModel.UpdateScrub(_lastFraction);
    }

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is not { IsScrubbing: true })
            return;

        _lastFraction = FractionAt(e);
        e.Pointer.Capture(null);
        _viewModel.EndScrub(_lastFraction);
        e.Handled = true;
    }
}
