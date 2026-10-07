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
    private Rect _appliedBounds;
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

        // The side bubble opens what it represents: the timer, or the island (mic/camera details).
        Bubble.Tapped += (_, _) =>
        {
            if (_viewModel is { BubbleShowsTimer: true })
                _viewModel.OpenTimerPickerCommand.Execute(null);
            else
                _viewModel?.Expand(peek: false);
        };

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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as IslandViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            ApplyInteractiveBounds(TargetBounds());
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
            case nameof(IslandViewModel.IslandWidth) or nameof(IslandViewModel.IslandHeight) or nameof(IslandViewModel.BubbleExtent):
                OnIslandSizeChanged(TargetBounds());
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
    private void OnIslandSizeChanged(Rect target)
    {
        var version = ++_boundsVersion;
        var union = _appliedBounds.Width > 0 ? target.Union(_appliedBounds) : target;

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

    /// <summary>The island (plus the side bubble when shown) in window coordinates, with room for the hover scale.</summary>
    private Rect TargetBounds()
    {
        if (_viewModel is null)
            return default;

        var width = _viewModel.IslandWidth;
        var left = (Width - width) / 2 - HoverAllowance;
        return new Rect(left, 0, width + _viewModel.BubbleExtent + HoverAllowance * 2,
            _viewModel.IslandHeight + TopInset + HoverAllowance);
    }

    private void ApplyInteractiveBounds(Rect rect)
    {
        _appliedBounds = rect;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var scale = RenderScaling;
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
