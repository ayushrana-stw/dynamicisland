using Avalonia.Animation.Easings;

namespace DynamicIsland.App.Controls;

/// <summary>
/// Damped-spring easing, parameterised like SwiftUI's <c>.spring(response:dampingFraction:)</c>.
/// Use with a transition whose <c>Duration</c> is long enough for the spring to settle (≈1.4 × Response).
/// </summary>
public sealed class SpringEase : Easing
{
    /// <summary>Seconds for one undamped oscillation; lower is snappier.</summary>
    public double Response { get; set; } = 0.42;

    /// <summary>1 = no overshoot; 0.7–0.8 gives a subtle bounce.</summary>
    public double DampingFraction { get; set; } = 0.76;

    /// <summary>Must match the transition's duration, in seconds.</summary>
    public double Duration { get; set; } = 0.6;

    public override double Ease(double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;

        var t = progress * Duration;
        var omega = 2 * Math.PI / Response;
        var zeta = Math.Clamp(DampingFraction, 0.05, 0.999);
        var omegaD = omega * Math.Sqrt(1 - zeta * zeta);
        var decay = Math.Exp(-zeta * omega * t);

        return 1 - decay * (Math.Cos(omegaD * t) + zeta * omega / omegaD * Math.Sin(omegaD * t));
    }
}
