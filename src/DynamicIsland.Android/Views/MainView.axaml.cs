using Avalonia.Controls;
using DynamicIsland.Droid.ViewModels;

namespace DynamicIsland.Droid.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        ViewModel = new MainViewModel();
        Settings.DataContext = ViewModel.Settings;
        Permissions.DataContext = ViewModel;
    }

    public MainViewModel ViewModel { get; }
}
