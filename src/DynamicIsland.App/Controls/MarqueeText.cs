using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;

namespace DynamicIsland.App.Controls;

/// <summary>
/// Single-line text that, when too long to fit, glides left to reveal the rest, pauses, and returns.
/// It only animates while <see cref="IsActive"/> is true and the text overflows, so it costs nothing otherwise.
/// Font properties (size, weight, colour) are inherited, so style it like a TextBlock's parent.
/// </summary>
public sealed class MarqueeText : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<MarqueeText, bool>(nameof(IsActive));

    // Inherited text properties, passed down to the inner TextBlock.
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<MarqueeText>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<MarqueeText>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<MarqueeText>();
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<MarqueeText>();

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Scroll speed in pixels per second.</summary>
    private const double Speed = 32;

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly TranslateTransform _shift = new();
    private CancellationTokenSource? _loop;
    private double _overflow;

    static MarqueeText()
    {
        ClipToBoundsProperty.OverrideDefaultValue<MarqueeText>(true);
    }

    public MarqueeText()
    {
        _text.RenderTransform = _shift;
        _text.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));
        LogicalChildren.Add(_text);
        VisualChildren.Add(_text);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var desired = _text.DesiredSize;
        return new Size(Math.Min(desired.Width, availableSize.Width), desired.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(0, 0, Math.Max(_text.DesiredSize.Width, finalSize.Width), finalSize.Height));

        var overflow = Math.Max(0, _text.DesiredSize.Width - finalSize.Width);
        if (Math.Abs(overflow - _overflow) > 0.5)
        {
            _overflow = overflow;
            Restart();
        }

        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IsActiveProperty)
            Restart();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    private void Stop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _shift.X = 0;
    }

    private void Restart()
    {
        Stop();
        if (!IsActive || _overflow <= 0 || VisualRoot is null)
            return;

        _loop = new CancellationTokenSource();
        _ = RunAsync(_overflow, _loop.Token);
    }

    private async Task RunAsync(double distance, CancellationToken token)
    {
        var travel = TimeSpan.FromSeconds(distance / Speed);
        var glide = Slide(0, -distance, travel, new LinearEasing());
        var back = Slide(-distance, 0, TimeSpan.FromMilliseconds(450), new CubicEaseInOut());

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                await glide.RunAsync(_shift, token);
                await Task.Delay(TimeSpan.FromSeconds(1.4), token);
                await back.RunAsync(_shift, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped: text changed, became inactive, or left the screen.
        }
    }

    private static Animation Slide(double from, double to, TimeSpan duration, Easing easing) => new()
    {
        Duration = duration,
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, to) } },
        },
    };
}
