namespace DynamicIsland.Core.Timers;

public enum TimerKind
{
    Countdown,
    Stopwatch,
}

public enum TimerState
{
    Idle,
    Running,
    Paused,
    Finished,
}

/// <summary>
/// Countdown timer or stopwatch. Pure logic: the caller supplies the clock,
/// so it does no background work and is easy to test.
/// </summary>
public sealed class IslandTimer
{
    private TimeSpan _accumulated;       // Time counted before the current run.
    private DateTimeOffset _runStartedAt;

    public TimerKind Kind { get; private set; }
    public TimerState State { get; private set; }

    /// <summary>Countdown length; zero for a stopwatch.</summary>
    public TimeSpan Duration { get; private set; }

    public bool IsActive => State is TimerState.Running or TimerState.Paused or TimerState.Finished;

    public void StartCountdown(TimeSpan duration, DateTimeOffset now)
    {
        Kind = TimerKind.Countdown;
        Duration = duration;
        Begin(now);
    }

    public void StartStopwatch(DateTimeOffset now)
    {
        Kind = TimerKind.Stopwatch;
        Duration = TimeSpan.Zero;
        Begin(now);
    }

    public void Pause(DateTimeOffset now)
    {
        if (State != TimerState.Running)
            return;

        _accumulated += now - _runStartedAt;
        State = TimerState.Paused;
    }

    public void Resume(DateTimeOffset now)
    {
        if (State != TimerState.Paused)
            return;

        _runStartedAt = now;
        State = TimerState.Running;
    }

    public void AddTime(TimeSpan extra, DateTimeOffset now)
    {
        if (Kind != TimerKind.Countdown || State == TimerState.Idle)
            return;

        if (State == TimerState.Finished)
        {
            // Extending a finished timer restarts it with just the extra time.
            Duration = extra;
            Begin(now);
            return;
        }

        Duration += extra;
    }

    public void Stop()
    {
        State = TimerState.Idle;
        _accumulated = TimeSpan.Zero;
    }

    public TimeSpan Elapsed(DateTimeOffset now) =>
        State == TimerState.Running ? _accumulated + (now - _runStartedAt) : _accumulated;

    public TimeSpan Remaining(DateTimeOffset now)
    {
        var remaining = Duration - Elapsed(now);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    /// <summary>0–1 share of the countdown used so far (always 0 for a stopwatch).</summary>
    public double Progress(DateTimeOffset now) =>
        Kind == TimerKind.Countdown && Duration > TimeSpan.Zero
            ? Math.Clamp(Elapsed(now) / Duration, 0, 1)
            : 0;

    /// <summary>Advances the state; returns true exactly once, when a countdown reaches zero.</summary>
    public bool Update(DateTimeOffset now)
    {
        if (Kind != TimerKind.Countdown || State != TimerState.Running || Elapsed(now) < Duration)
            return false;

        _accumulated = Duration;
        State = TimerState.Finished;
        return true;
    }

    private void Begin(DateTimeOffset now)
    {
        _accumulated = TimeSpan.Zero;
        _runStartedAt = now;
        State = TimerState.Running;
    }
}
