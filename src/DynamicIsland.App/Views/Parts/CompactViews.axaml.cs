using Avalonia.Controls;
using Avalonia.Threading;
using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Views.Parts;

public partial class CompactViews : UserControl
{
    private IslandViewModel? _viewModel;

    public CompactViews()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.TrackChanged -= OnTrackChanged;

        _viewModel = DataContext as IslandViewModel;

        if (_viewModel is not null)
            _viewModel.TrackChanged += OnTrackChanged;
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        CompactArt.Classes.Add("bump");
        DispatcherTimer.RunOnce(() => CompactArt.Classes.Remove("bump"), TimeSpan.FromMilliseconds(170));
    }
}
