using Avalonia.Controls;
using Avalonia.Threading;
using DynamicIsland.App.ViewModels;

namespace DynamicIsland.App.Views.Parts;

public partial class NotificationCard : UserControl
{
    private IslandViewModel? _viewModel;

    public NotificationCard()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.NotificationArrived -= OnNotificationArrived;

        _viewModel = DataContext as IslandViewModel;

        if (_viewModel is not null)
            _viewModel.NotificationArrived += OnNotificationArrived;
    }

    private void OnNotificationArrived(object? sender, EventArgs e)
    {
        Icon.Classes.Add("bump");
        DispatcherTimer.RunOnce(() => Icon.Classes.Remove("bump"), TimeSpan.FromMilliseconds(170));
    }
}
