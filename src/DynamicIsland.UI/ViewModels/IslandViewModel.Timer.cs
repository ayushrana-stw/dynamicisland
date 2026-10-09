using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Timers;

namespace DynamicIsland.UI.ViewModels;

public sealed partial class IslandViewModel
{
    /// <summary>Width of the progress bar in the expanded timer view.</summary>
    public const double TimerTrackWidth = 324;

    private readonly IslandTimer _timer = new();

    /// <summary>Plays the platform's alert sound when a countdown ends.</summary>
    public Action? PlayAlert { get; init; }

    [ObservableProperty] public partial bool TimerActive { get; private set; }
    [ObservableProperty] public partial bool TimerRunning { get; private set; }
    [ObservableProperty] public partial bool TimerFinished { get; private set; }
    [ObservableProperty] public partial bool TimerIsCountdown { get; private set; }
    [ObservableProperty] public partial string TimerText { get; private set; } = "";
    [ObservableProperty] public partial string TimerLabel { get; private set; } = "";
    [ObservableProperty] public partial double TimerBarWidth { get; private set; }

    /// <summary>Remaining share of the countdown as an arc angle for the side bubble (0–360).</summary>
    [ObservableProperty] public partial double TimerSweep { get; private set; }

    /// <summary>Very short label for the side bubble, e.g. "12" (minutes) or "45s".</summary>
    [ObservableProperty] public partial string TimerShortText { get; private set; } = "";

    public bool TimerPaused => TimerActive && !TimerRunning && !TimerFinished;

    [RelayCommand]
    private void OpenTimerPicker() => NavigateTo(TimerActive ? ExpandedPage.Timer : ExpandedPage.TimerPicker);

    /// <summary>Starts a countdown; the parameter is minutes (from the preset buttons).</summary>
    [RelayCommand]
    public void StartTimer(string minutes)
    {
        if (!double.TryParse(minutes, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            return;

        _timer.StartCountdown(TimeSpan.FromMinutes(value), DateTimeOffset.Now);
        TimerLabel = value >= 1 ? $"Timer · {value:0} min" : "Timer";
        AfterTimerChange(ExpandedPage.Timer);
    }

    [RelayCommand]
    public void StartStopwatch()
    {
        _timer.StartStopwatch(DateTimeOffset.Now);
        TimerLabel = "Stopwatch";
        AfterTimerChange(ExpandedPage.Timer);
    }

    [RelayCommand]
    private void PauseResumeTimer()
    {
        if (_timer.State == TimerState.Running)
            _timer.Pause(DateTimeOffset.Now);
        else
            _timer.Resume(DateTimeOffset.Now);

        AfterTimerChange(null);
    }

    [RelayCommand]
    private void AddMinute()
    {
        _timer.AddTime(TimeSpan.FromMinutes(1), DateTimeOffset.Now);
        AfterTimerChange(null);
    }

    [RelayCommand]
    public void StopTimer()
    {
        _timer.Stop();
        AfterTimerChange(ExpandedPage.Auto);
    }

    private void AfterTimerChange(ExpandedPage? page)
    {
        TickTimer();
        if (page is { } target)
            _page = target;
        UpdateShape();
    }

    /// <summary>Refreshes the timer display; called every second while a timer is active.</summary>
    private void TickTimer()
    {
        var now = DateTimeOffset.Now;
        if (_timer.Update(now))
            OnTimerFinished();

        TimerActive = _timer.IsActive;
        TimerRunning = _timer.State == TimerState.Running;
        TimerFinished = _timer.State == TimerState.Finished;
        TimerIsCountdown = _timer.Kind == TimerKind.Countdown;
        OnPropertyChanged(nameof(TimerPaused));

        if (!TimerActive)
            return;

        var shown = TimerIsCountdown ? _timer.Remaining(now) : _timer.Elapsed(now);
        TimerText = shown.TotalHours >= 1 ? shown.ToString(@"h\:mm\:ss") : shown.ToString(@"m\:ss");

        var progress = _timer.Progress(now);
        TimerBarWidth = TimerIsCountdown ? TimerTrackWidth * progress : 0;
        TimerSweep = TimerIsCountdown ? 360 * (1 - progress) : 360;
        TimerShortText = shown.TotalMinutes >= 1 ? $"{Math.Ceiling(shown.TotalMinutes - 0.0001):0}" : $"{shown.Seconds}s";
    }

    private void OnTimerFinished()
    {
        if (Settings.TimerSound)
            PlayAlert?.Invoke();

        TimerLabel = "Time's up";
        _page = ExpandedPage.Timer;
        _notificationQueue.Clear();
        HasNotification = false;

        // Stays open until the user dismisses it or the usual idle time passes (plus a little).
        Expand(peek: true);
        RestartCollapseTimer(extraSeconds: 6);
    }

    partial void OnTimerActiveChanged(bool value) => UpdateShape();
    partial void OnTimerFinishedChanged(bool value) => UpdateShape();
}
