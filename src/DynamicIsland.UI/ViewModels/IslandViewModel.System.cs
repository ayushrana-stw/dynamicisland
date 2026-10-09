using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core.SystemStatus;

namespace DynamicIsland.UI.ViewModels;

public sealed partial class IslandViewModel
{
    /// <summary>Width of the level bar in the HUD.</summary>
    public const double HudTrackWidth = 150;

    private static readonly IBrush HudWhite = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7));
    private static readonly IBrush HudYellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly IBrush HudGreen = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly IBrush HudRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly IBrush PrivacyOrange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));

    private readonly ISystemStatusService _system;
    private readonly DispatcherTimer _hudTimer;
    private PowerStatus? _lastPower;

    // ---- HUD (volume, brightness, battery) -----------------------------------------------------

    [ObservableProperty] public partial bool HudVisible { get; private set; }
    [ObservableProperty] public partial Geometry? HudIcon { get; private set; }
    [ObservableProperty] public partial IBrush HudAccent { get; private set; } = HudWhite;
    [ObservableProperty] public partial double HudBarWidth { get; private set; }
    [ObservableProperty] public partial string HudText { get; private set; } = "";
    [ObservableProperty] public partial bool HudHasBar { get; private set; }

    /// <summary>Shows a brief level/status pill. Public so the demo and snapshots can drive it.</summary>
    public void PresentHud(string iconKey, IBrush accent, double? level, string text, TimeSpan duration)
    {
        // The expanded island is busy with something the user opened; don't cover it.
        if (IsExpanded)
            return;

        HudIcon = FindGeometry(iconKey);
        HudAccent = accent;
        HudHasBar = level is not null;
        HudBarWidth = HudTrackWidth * Math.Clamp(level ?? 0, 0, 1);
        HudText = text;

        _hudTimer.Stop();
        _hudTimer.Interval = duration;
        _hudTimer.Start();

        if (!HudVisible)
        {
            HudVisible = true;
            UpdateShape();
        }
    }

    private void HideHud()
    {
        _hudTimer.Stop();
        if (!HudVisible)
            return;

        HudVisible = false;
        UpdateShape();
    }

    private void OnVolumeChanged(object? sender, VolumeLevel volume) => Dispatcher.UIThread.Post(() =>
    {
        if (!Settings.ShowVolume)
            return;

        var muted = volume.IsMuted || volume.Level <= 0.001;
        PresentHud(muted ? "IconVolumeMuted" : "IconVolume", muted ? HudRed : HudWhite,
            muted ? 0 : volume.Level, muted ? "Muted" : $"{Math.Round(volume.Level * 100)}", TimeSpan.FromSeconds(1.6));
    });

    private void OnBrightnessChanged(object? sender, double level) => Dispatcher.UIThread.Post(() =>
    {
        if (Settings.ShowBrightness)
            PresentHud("IconBrightness", HudYellow, level, $"{Math.Round(level * 100)}", TimeSpan.FromSeconds(1.6));
    });

    private void OnPowerChanged(object? sender, PowerStatus status) => Dispatcher.UIThread.Post(() =>
    {
        var previous = _lastPower;
        _lastPower = status;

        if (!Settings.ShowBatteryAlerts || previous is null || status.Percent is not { } percent)
            return;

        if (status.IsOnAc && !previous.Value.IsOnAc)
        {
            PresentHud("IconBatteryCharging", HudGreen, percent / 100.0, $"Charging · {percent}%", TimeSpan.FromSeconds(2.5));
        }
        else if (!status.IsOnAc && previous.Value.Percent is { } before && CrossedDown(before, percent))
        {
            PresentHud("IconBatteryAlert", HudRed, percent / 100.0, $"Battery low · {percent}%", TimeSpan.FromSeconds(4));
        }
    });

    /// <summary>True when the charge drops through 20% or 10%.</summary>
    private static bool CrossedDown(int before, int now) =>
        (before > 20 && now <= 20) || (before > 10 && now <= 10);

    partial void OnHudVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowHud));

    private static Geometry? FindGeometry(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true ? value as Geometry : null;

    // ---- Privacy (microphone / camera in use) ----------------------------------------------------

    [ObservableProperty] public partial bool PrivacyInUse { get; private set; }
    [ObservableProperty] public partial bool PrivacyUsesCamera { get; private set; }
    [ObservableProperty] public partial string PrivacyText { get; private set; } = "";
    [ObservableProperty] public partial IBrush PrivacyBrush { get; private set; } = PrivacyOrange;

    public bool ShowPrivacy => PrivacyInUse && Settings.ShowPrivacyIndicator;

    private void OnPrivacyChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        ApplyPrivacy();
        UpdateShape();
    });

    /// <summary>Applies a privacy state; public so the demo and snapshots can simulate a call.</summary>
    public void ApplyPrivacy(IReadOnlyList<PrivacyUse>? uses = null)
    {
        uses ??= _system.PrivacyUses;

        PrivacyInUse = uses.Count > 0;
        PrivacyUsesCamera = uses.Any(u => u.Camera);

        // Same colours as iOS: green for camera, orange for microphone only.
        PrivacyBrush = PrivacyUsesCamera ? HudGreen : PrivacyOrange;

        var apps = string.Join(", ", uses.Select(u => u.AppName).Distinct());
        var device = (uses.Any(u => u.Microphone), PrivacyUsesCamera) switch
        {
            (true, true) => "microphone and camera",
            (false, true) => "camera",
            _ => "microphone",
        };
        PrivacyText = PrivacyInUse ? $"{apps} is using your {device}" : "";

        OnPropertyChanged(nameof(ShowPrivacy));
    }
}
