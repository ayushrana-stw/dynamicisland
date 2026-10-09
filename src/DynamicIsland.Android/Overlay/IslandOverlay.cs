using System.ComponentModel;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicIsland.UI.ViewModels;
using DynamicIsland.UI.Views;
using Format = Android.Graphics.Format;
using Rect = Avalonia.Rect;

namespace DynamicIsland.Droid.Overlay;

/// <summary>
/// Floats the shared <see cref="IslandView"/> over other apps in a system overlay window.
/// </summary>
/// <remarks>
/// Android stacks the status bar above ordinary app overlays and gives it every touch at the top of the screen.
/// Hosted by the accessibility service, the window can sit above the status bar, around the camera; hosted by
/// the foreground service, it sits just below the status bar so it can still be tapped.
/// The island is laid out on the same fixed canvas as the desktop window, centred at the top of the screen.
/// The overlay window only covers the island's current bounds, so touches everywhere else reach the apps
/// below; the canvas is shifted inside the window so the island never moves when the window resizes.
/// </remarks>
internal sealed class IslandOverlay : IDisposable
{
    private const double TopInset = 4;          // Island's margin at the top of the canvas.
    private const double HoverAllowance = 8;    // Room for the press/hover scale-up.
    private const double CanvasWidth = IslandViewModel.MaxWidth + HoverAllowance * 2;
    private const double CanvasHeight = IslandViewModel.MaxHeight + TopInset + HoverAllowance;
    private static readonly TimeSpan ShrinkSettleTime = TimeSpan.FromMilliseconds(650);

    private readonly Context _context;
    private readonly IWindowManager _windowManager;
    private readonly IslandViewModel _viewModel;
    private readonly IslandView _islandView;
    private readonly OverlayView _view;
    private readonly WindowManagerLayoutParams _layout;

    private Rect _appliedBounds;
    private int _boundsVersion;
    private bool _attached;
    private readonly bool _belowStatusBar;

    /// <param name="context">The hosting service; accessibility overlays must use the accessibility service itself.</param>
    /// <param name="windowType">ApplicationOverlay, or AccessibilityOverlay to sit above the status bar.</param>
    public IslandOverlay(Context context, IslandViewModel viewModel, WindowManagerTypes windowType)
    {
        _context = context;
        _belowStatusBar = windowType != WindowManagerTypes.AccessibilityOverlay;
        _windowManager = context.GetSystemService(Context.WindowService).JavaCast<IWindowManager>()!;
        _viewModel = viewModel;

        _islandView = new IslandView
        {
            DataContext = viewModel,
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        // A context menu would be clipped by the small overlay window; a long press opens settings instead,
        // and the expanded island has the same commands as buttons.
        _islandView.IslandSurface.ContextMenu = null;
        _islandView.IslandSurface.Holding += (_, e) =>
        {
            if (e.HoldingState == Avalonia.Input.HoldingState.Started)
                LongPressed?.Invoke(this, EventArgs.Empty);
        };

        var canvas = new Panel { ClipToBounds = true, Children = { _islandView } };

        // Avalonia paints its root with the theme background, plus a white "transparency fallback" because it
        // can only make activities transparent. The overlay must show the app below instead. The fallback brush
        // is copied into the template only when the transparency level changes, so clear the border itself.
        canvas.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(canvas) is not { } root)
                return;

            root.Background = null;
            foreach (var border in root.GetVisualChildren().SelectMany(c => c.GetVisualChildren()).OfType<Border>())
            {
                if (border.Name == "PART_TransparencyFallback")
                    border.Background = null;
            }
        };

        _view = new OverlayView(context) { Content = canvas };
        _view.OutsideTouched += (_, _) =>
        {
            // Touching anywhere else closes the island, like a notification banner.
            if (_viewModel.IsExpanded)
                _viewModel.Collapse();
        };

        _layout = new WindowManagerLayoutParams(
            1, 1,
            windowType,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen
                | WindowManagerFlags.WatchOutsideTouch | WindowManagerFlags.HardwareAccelerated,
            Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left,
            Title = "Dynamic Island",
        };

        // Let the island sit in the camera cut-out area like the real one.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            _layout.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always;
        else if (OperatingSystem.IsAndroidVersionAtLeast(28))
            _layout.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Raised when the user long-presses the island.</summary>
    public event EventHandler? LongPressed;

    public void Show()
    {
        if (_attached)
            return;

        _appliedBounds = TargetBounds();
        ApplyBounds(_appliedBounds);
        _windowManager.AddView(_view, _layout);
        _attached = true;
    }

    /// <summary>Re-centres the island, e.g. after the screen rotates or the "distance from top" setting changes.</summary>
    public void Reposition() => ApplyBounds(_appliedBounds);

    // ---- Window bounds -----------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IslandViewModel.IslandWidth) or nameof(IslandViewModel.IslandHeight) or nameof(IslandViewModel.BubbleExtent):
                OnIslandSizeChanged(TargetBounds());
                break;

            case nameof(IslandViewModel.Settings):
                Reposition();
                break;
        }
    }

    /// <summary>
    /// Grows the window immediately; shrinks it only after the spring animation settles so the island never looks clipped.
    /// </summary>
    private void OnIslandSizeChanged(Rect target)
    {
        var version = ++_boundsVersion;
        var union = _appliedBounds.Width > 0 ? target.Union(_appliedBounds) : target;

        ApplyBounds(union);

        if (union != target)
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (version == _boundsVersion)
                    ApplyBounds(target);
            }, ShrinkSettleTime);
        }
    }

    /// <summary>The island (plus the side bubble when shown) in canvas coordinates, with room for the scale-up.</summary>
    private Rect TargetBounds()
    {
        var width = _viewModel.IslandWidth;
        var left = (CanvasWidth - width) / 2 - HoverAllowance;
        return new Rect(left, 0, width + _viewModel.BubbleExtent + HoverAllowance * 2,
            _viewModel.IslandHeight + TopInset + HoverAllowance);
    }

    /// <summary>Sizes and places the window over <paramref name="bounds"/> and shifts the canvas to match.</summary>
    private void ApplyBounds(Rect bounds)
    {
        _appliedBounds = bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var density = _context.Resources!.DisplayMetrics!.Density;
        var screenWidth = ScreenWidthPixels();

        // Where the canvas's top-left corner sits on screen, in pixels.
        var canvasX = (screenWidth - CanvasWidth * density) / 2;
        var canvasY = (_viewModel.Settings.VerticalOffset - TopInset) * density + (_belowStatusBar ? StatusBarHeightPixels() : 0);

        var x = (int)Math.Floor(canvasX + bounds.X * density);
        var y = Math.Max(0, (int)Math.Floor(canvasY + bounds.Y * density));
        _layout.X = x;
        _layout.Y = y;
        _layout.Width = (int)Math.Ceiling(bounds.Width * density);
        _layout.Height = (int)Math.Ceiling(bounds.Height * density);

        _islandView.Margin = new Thickness((canvasX - x) / density, (canvasY - y) / density, 0, 0);

        if (_attached)
            _windowManager.UpdateViewLayout(_view, _layout);
    }

    private int StatusBarHeightPixels()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            return _windowManager.CurrentWindowMetrics.WindowInsets.GetInsetsIgnoringVisibility(WindowInsets.Type.StatusBars()).Top;

        var id = _context.Resources!.GetIdentifier("status_bar_height", "dimen", "android");
        return id > 0 ? _context.Resources.GetDimensionPixelSize(id) : 0;
    }

    private int ScreenWidthPixels() =>
        OperatingSystem.IsAndroidVersionAtLeast(30)
            ? _windowManager.CurrentWindowMetrics.Bounds.Width()
            : _context.Resources!.DisplayMetrics!.WidthPixels;

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_attached)
        {
            _windowManager.RemoveView(_view);
            _attached = false;
        }

        _view.Content = null;
    }

    /// <summary>The Avalonia host view; also reports touches outside the island.</summary>
    private sealed class OverlayView : AvaloniaView
    {
        public OverlayView(Context context) : base(context)
        {
            // Avalonia draws into a SurfaceView. Put it above the window and give it an alpha channel
            // so the space around the rounded island shows the app underneath instead of black.
            if (GetChildAt(0) is SurfaceView surface)
            {
                surface.SetZOrderOnTop(true);
                surface.Holder?.SetFormat(Format.Translucent);
            }
        }

        public event EventHandler? OutsideTouched;

        public override bool DispatchTouchEvent(MotionEvent? e)
        {
            if (e?.Action == MotionEventActions.Outside)
            {
                OutsideTouched?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return base.DispatchTouchEvent(e);
        }
    }
}
