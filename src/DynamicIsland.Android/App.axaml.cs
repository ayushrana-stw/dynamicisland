using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DynamicIsland.Droid.Views;

namespace DynamicIsland.Droid;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The activity shows settings and permissions; the island itself lives in IslandOverlayService.
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();

        base.OnFrameworkInitializationCompleted();
    }
}
