using System.Windows.Threading;

namespace DesktopPet.Services;

public enum FocusTimerState
{
    Idle,
    Focusing,
    FocusPaused,
    Break,
    BreakPaused
}

/// <summary>
/// Deadline-based Pomodoro focus timer. Uses a single one-shot completion timer
/// rather than ticking every frame or polling continuously.
/// </summary>
public sealed class FocusTimerService : IDisposable
{
    private FocusTimerState _state = FocusTimerState.Idle;
    private DateTime? _targetDeadlineUtc;
    private TimeSpan? _remainingOnPause;
    private DispatcherTimer? _completionTimer;
    private readonly Dispatcher _dispatcher;

    public int FocusDurationMinutes { get; set; } = 25;
    public int BreakDurationMinutes { get; set; } = 5;

    public FocusTimerState State => _state;
    public bool IsActive => _state != FocusTimerState.Idle;
    public bool IsPaused => _state is FocusTimerState.FocusPaused or FocusTimerState.BreakPaused;
    public bool IsBreak => _state is FocusTimerState.Break or FocusTimerState.BreakPaused;

    public DateTime? TargetDeadlineUtc => _targetDeadlineUtc;

    public event Action<FocusTimerState>? StateChanged;
    public event Action? FocusCompleted;
    public event Action? BreakCompleted;

    public FocusTimerService(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>Remaining time for the active session.</summary>
    public TimeSpan RemainingTime
    {
        get
        {
            if (_state is FocusTimerState.Focusing or FocusTimerState.Break)
            {
                if (_targetDeadlineUtc.HasValue)
                {
                    var rem = _targetDeadlineUtc.Value - DateTime.UtcNow;
                    return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
                }
                return TimeSpan.Zero;
            }

            if (_state is FocusTimerState.FocusPaused or FocusTimerState.BreakPaused)
            {
                return _remainingOnPause ?? TimeSpan.Zero;
            }

            return TimeSpan.Zero;
        }
    }

    /// <summary>Human-readable string MM:SS for display.</summary>
    public string FormattedRemaining
    {
        get
        {
            var t = RemainingTime;
            int totalMins = (int)t.TotalMinutes;
            return $"{totalMins:D2}:{t.Seconds:D2}";
        }
    }

    /// <summary>Start a new focus session.</summary>
    public void StartFocus(int? customMinutes = null)
    {
        StopCompletionTimer();
        int mins = Math.Max(1, customMinutes ?? FocusDurationMinutes);
        var duration = TimeSpan.FromMinutes(mins);

        _targetDeadlineUtc = DateTime.UtcNow + duration;
        _remainingOnPause = null;
        _state = FocusTimerState.Focusing;

        ScheduleCompletion(duration);
        StateChanged?.Invoke(_state);
        DebugLog.Write($"[FocusTimer] Focus session started for {mins}m (deadline: {_targetDeadlineUtc:O})");
    }

    /// <summary>Pause the active session.</summary>
    public void Pause()
    {
        if (_state == FocusTimerState.Focusing)
        {
            StopCompletionTimer();
            _remainingOnPause = RemainingTime;
            _targetDeadlineUtc = null;
            _state = FocusTimerState.FocusPaused;
            StateChanged?.Invoke(_state);
            DebugLog.Write($"[FocusTimer] Focus paused. Remaining: {FormattedRemaining}");
        }
        else if (_state == FocusTimerState.Break)
        {
            StopCompletionTimer();
            _remainingOnPause = RemainingTime;
            _targetDeadlineUtc = null;
            _state = FocusTimerState.BreakPaused;
            StateChanged?.Invoke(_state);
            DebugLog.Write($"[FocusTimer] Break paused. Remaining: {FormattedRemaining}");
        }
    }

    /// <summary>Resume a paused session.</summary>
    public void Resume()
    {
        if (_state == FocusTimerState.FocusPaused)
        {
            var rem = _remainingOnPause ?? TimeSpan.FromMinutes(FocusDurationMinutes);
            if (rem <= TimeSpan.Zero) rem = TimeSpan.FromMinutes(1);
            _targetDeadlineUtc = DateTime.UtcNow + rem;
            _remainingOnPause = null;
            _state = FocusTimerState.Focusing;
            ScheduleCompletion(rem);
            StateChanged?.Invoke(_state);
            DebugLog.Write($"[FocusTimer] Focus resumed. Remaining: {FormattedRemaining}");
        }
        else if (_state == FocusTimerState.BreakPaused)
        {
            var rem = _remainingOnPause ?? TimeSpan.FromMinutes(BreakDurationMinutes);
            if (rem <= TimeSpan.Zero) rem = TimeSpan.FromMinutes(1);
            _targetDeadlineUtc = DateTime.UtcNow + rem;
            _remainingOnPause = null;
            _state = FocusTimerState.Break;
            ScheduleCompletion(rem);
            StateChanged?.Invoke(_state);
            DebugLog.Write($"[FocusTimer] Break resumed. Remaining: {FormattedRemaining}");
        }
    }

    /// <summary>Reset the current session to its initial full duration and start over.</summary>
    public void Reset()
    {
        if (_state is FocusTimerState.Break or FocusTimerState.BreakPaused)
        {
            StartBreak();
        }
        else
        {
            StartFocus();
        }
    }

    /// <summary>Cancel any active timer and return to Idle.</summary>
    public void Cancel()
    {
        StopCompletionTimer();
        _state = FocusTimerState.Idle;
        _targetDeadlineUtc = null;
        _remainingOnPause = null;
        StateChanged?.Invoke(_state);
        DebugLog.Write("[FocusTimer] Focus timer canceled.");
    }

    /// <summary>Start a break session.</summary>
    public void StartBreak(int? customMinutes = null)
    {
        StopCompletionTimer();
        int mins = Math.Max(1, customMinutes ?? BreakDurationMinutes);
        var duration = TimeSpan.FromMinutes(mins);

        _targetDeadlineUtc = DateTime.UtcNow + duration;
        _remainingOnPause = null;
        _state = FocusTimerState.Break;

        ScheduleCompletion(duration);
        StateChanged?.Invoke(_state);
        DebugLog.Write($"[FocusTimer] Break started for {mins}m (deadline: {_targetDeadlineUtc:O})");
    }

    private void ScheduleCompletion(TimeSpan delay)
    {
        StopCompletionTimer();
        if (delay <= TimeSpan.Zero)
        {
            OnTimerFired();
            return;
        }

        _completionTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = delay
        };
        _completionTimer.Tick += (_, _) => OnTimerFired();
        _completionTimer.Start();
    }

    private void StopCompletionTimer()
    {
        if (_completionTimer != null)
        {
            _completionTimer.Stop();
            _completionTimer = null;
        }
    }

    private void OnTimerFired()
    {
        StopCompletionTimer();

        if (_state == FocusTimerState.Focusing)
        {
            DebugLog.Write("[FocusTimer] Focus session deadline reached!");
            _state = FocusTimerState.Idle;
            _targetDeadlineUtc = null;
            _remainingOnPause = null;
            StateChanged?.Invoke(_state);
            FocusCompleted?.Invoke();
        }
        else if (_state == FocusTimerState.Break)
        {
            DebugLog.Write("[FocusTimer] Break session deadline reached!");
            _state = FocusTimerState.Idle;
            _targetDeadlineUtc = null;
            _remainingOnPause = null;
            StateChanged?.Invoke(_state);
            BreakCompleted?.Invoke();
        }
    }

    /// <summary>Save timer state to AppSettings for persistence across restarts.</summary>
    public void SaveState(AppSettings settings)
    {
        settings.FocusDurationMinutes = FocusDurationMinutes;
        settings.BreakDurationMinutes = BreakDurationMinutes;
        settings.FocusTimerActive = IsActive;
        settings.FocusTimerState = _state.ToString();
        settings.FocusTimerDeadlineUtc = _targetDeadlineUtc;
        settings.FocusTimerRemainingSeconds = RemainingTime.TotalSeconds;
    }

    /// <summary>Restore timer state from AppSettings on startup.</summary>
    public void LoadState(AppSettings settings)
    {
        FocusDurationMinutes = settings.FocusDurationMinutes > 0 ? settings.FocusDurationMinutes : 25;
        BreakDurationMinutes = settings.BreakDurationMinutes > 0 ? settings.BreakDurationMinutes : 5;

        if (!settings.FocusTimerActive)
        {
            Cancel();
            return;
        }

        if (settings.FocusTimerState == nameof(FocusTimerState.FocusPaused))
        {
            _state = FocusTimerState.FocusPaused;
            _remainingOnPause = TimeSpan.FromSeconds(Math.Max(0, settings.FocusTimerRemainingSeconds));
            _targetDeadlineUtc = null;
            StateChanged?.Invoke(_state);
            return;
        }

        if (settings.FocusTimerState == nameof(FocusTimerState.BreakPaused))
        {
            _state = FocusTimerState.BreakPaused;
            _remainingOnPause = TimeSpan.FromSeconds(Math.Max(0, settings.FocusTimerRemainingSeconds));
            _targetDeadlineUtc = null;
            StateChanged?.Invoke(_state);
            return;
        }

        if (settings.FocusTimerDeadlineUtc.HasValue)
        {
            var rem = settings.FocusTimerDeadlineUtc.Value - DateTime.UtcNow;
            if (rem > TimeSpan.Zero)
            {
                _targetDeadlineUtc = settings.FocusTimerDeadlineUtc.Value;
                _remainingOnPause = null;
                _state = settings.FocusTimerState == nameof(FocusTimerState.Break)
                    ? FocusTimerState.Break
                    : FocusTimerState.Focusing;
                ScheduleCompletion(rem);
                StateChanged?.Invoke(_state);
                return;
            }
        }

        // If deadline passed while app was closed:
        Cancel();
    }

    public void Dispose()
    {
        StopCompletionTimer();
    }
}
