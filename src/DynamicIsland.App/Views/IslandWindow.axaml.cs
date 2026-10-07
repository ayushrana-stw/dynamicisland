using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicIsland.App.Platform;
using DynamicIsland.App.ViewModels;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.Views;

public partial class IslandWindow : Window
{
    private const double TopInset = 4;          // Island's margin inside the window.
    private const double HoverAllowance = 8;    // Room for the hover scale-up.
    private static readonly TimeSpan ShrinkSettleTime = TimeSpan.FromMilliseconds(650);

    private readonly IIslandWindowPlatform _platform;
    private IslandViewModel? _viewModel;
    private Size _appliedBounds;
    private int _boundsVersion;
    private bool _fullScreenAppActive;

    // Required by the XAML loader / previewer.
    public IslandWindow() : this(new NullIslandWindowPlatform()) { }

    internal IslandWindow(IIslandWindowPlatform platform)
    {
        _platform = platform;
        InitializeComponent();

        Width = IslandViewModel.MaxWidth + HoverAllowance * 2;
        Height = IslandViewModel.MaxHeight + TopInset + HoverAllowance;

        Island.Tapped += OnIslandTapped;
        Island.PointerEntered += (_, _) => _viewModel?.OnPointerEntered();
        Island.PointerExited += (_, _) => _viewModel?.OnPointerExited();
        Island.PointerPressed += OnIslandPointerPressed;
        Island.PointerReleased += (_, _) => Island.Classes.Remove("pressing");
        Island.PointerCaptureLost += (_, _) => Island.Classes.Remove("pressing");

        _platform.HotkeyPressed += (_, _) => OpenFromKeyboard();
        _platform.FullScreenChanged += OnFullScreenChanged;
        Deactivated += OnWindowDeactivated;
    }

    /// <summary>Raised when a full-screen app hides or reveals the island.</summary>
    public event EventHandler? VisibilityPolicyChanged;

    /// <summary>True while a full-screen app is in front and the user asked to hide for it.</summary>
    public bool SuppressedByFullScreen =>
        _fullScreenAppActive && (_viewModel?.Settings.HideInFullScreen ?? true);

    public string? HotkeyDescription => _platform.HotkeyDescription;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.TrackChanged -= OnTrackChanged;
            _viewModel.NotificationArrived -= OnNotificationArrived;
        }

        _viewModel = DataContext as IslandViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.TrackChanged += OnTrackChanged;
            _viewModel.NotificationArrived += OnNotificationArrived;
        }
    }

    /// <summary>The artwork pops briefly; its spring transition carries it back.</summary>
    private void OnTrackChanged(object? sender, EventArgs e)
    {
        CompactArt.Classes.Add("bump");
        ExpandedArt.Classes.Add("bump");
        DispatcherTimer.RunOnce(() =>
        {
            CompactArt.Classes.Remove("bump");
            ExpandedArt.Classes.Remove("bump");
        }, TimeSpan.FromMilliseconds(170));
    }

    private void OnNotificationArrived(object? sender, EventArgs e)
    {
        NotificationIconBorder.Classes.Add("bump");
        DispatcherTimer.RunOnce(() => NotificationIconBorder.Classes.Remove("bump"), TimeSpan.FromMilliseconds(170));
    }

    private void OnIslandPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        Island.Classes.Add("pressing");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _platform.Attach(this);
        Screens.Changed += (_, _) => Reposition();
        ScalingChanged += (_, _) =>
        {
            Reposition();
            ApplyInteractiveBounds(_appliedBounds);
        };

        Reposition();
        if (_viewModel is not null)
            ApplyInteractiveBounds(new Size(_viewModel.IslandWidth, _viewModel.IslandHeight));
    }

    protected override void OnClosed(EventArgs e)
    {
        _platform.Dispose();
        base.OnClosed(e);
    }

    // ---- Placement -------------------------------------------------------------------------

    /// <summary>Centres the window at the top of the chosen display.</summary>
    public void Reposition()
    {
        var settings = _viewModel?.Settings ?? IslandSettings.Default;
        var screen = PickScreen(settings.DisplayIndex);
        if (screen is null)
            return;

        var scale = screen.Scaling;
        var bounds = screen.Bounds;
        var widthPx = (int)Math.Round(Width * scale);
        var x = bounds.X + (bounds.Width - widthPx) / 2;
        var y = bounds.Y + (int)Math.Round((settings.VerticalOffset - TopInset) * scale);

        Position = new PixelPoint(x, y);
    }

    private Screen? PickScreen(int displayIndex)
    {
        var all = Screens.All;
        if (displayIndex >= 0 && displayIndex < all.Count)
            return all[displayIndex];

        return Screens.Primary ?? all.FirstOrDefault();
    }

    // ---- Click-through ---------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IslandViewModel.IslandWidth) or nameof(IslandViewModel.IslandHeight):
                OnIslandSizeChanged(new Size(_viewModel!.IslandWidth, _viewModel.IslandHeight));
                break;

            case nameof(IslandViewModel.Settings):
                Reposition();
                VisibilityPolicyChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Keeps mouse input limited to the island. When growing, the input area grows immediately;
    /// when shrinking, it waits for the spring animation to finish so the island never looks clipped.
    /// </summary>
    private void OnIslandSizeChanged(Size target)
    {
        var version = ++_boundsVersion;
        var union = new Size(Math.Max(target.Width, _appliedBounds.Width), Math.Max(target.Height, _appliedBounds.Height));

        ApplyInteractiveBounds(union);

        if (union != target)
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (version == _boundsVersion)
                    ApplyInteractiveBounds(target);
            }, ShrinkSettleTime);
        }
    }

    private void ApplyInteractiveBounds(Size island)
    {
        _appliedBounds = island;
        if (island.Width <= 0 || island.Height <= 0)
            return;

        var scale = RenderScaling;
        var left = (Width - island.Width) / 2 - HoverAllowance;
        var rect = new Rect(left, 0, island.Width + HoverAllowance * 2, island.Height + TopInset + HoverAllowance);

        _platform.SetInteractiveBounds(new PixelRect(
            (int)Math.Floor(rect.X * scale),
            (int)Math.Floor(rect.Y * scale),
            (int)Math.Ceiling(rect.Width * scale),
            (int)Math.Ceiling(rect.Height * scale)));
    }

    // ---- Input -------------------------------------------------------------------------------

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _viewModel is null)
            return;

        switch (e.Key)
        {
            case Key.Escape when _viewModel.IsExpanded:
                _viewModel.Collapse();
                e.Handled = true;
                break;

            case Key.Enter or Key.Space when Island.IsFocused:
                _viewModel.ToggleExpanded();
                e.Handled = true;
                break;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Clicking anywhere else closes the island, like a flyout.
        if (_viewModel is { IsExpanded: true } && !Island.IsPointerOver)
            _viewModel.Collapse();
    }

    /// <summary>Global shortcut: show, expand, and give keyboard focus to the island.</summary>
    public void OpenFromKeyboard()
    {
        if (_viewModel is null)
            return;

        if (!IsVisible)
            Show();

        Activate();
        _viewModel.Expand(peek: false);
        Island.Focus(NavigationMethod.Tab);
    }

    private void OnFullScreenChanged(object? sender, bool active)
    {
        _fullScreenAppActive = active;
        VisibilityPolicyChanged?.Invoke(this, EventArgs.Empty);
    }
}
