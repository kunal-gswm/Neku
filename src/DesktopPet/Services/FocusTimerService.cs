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
/// Deadline-based Pomodoro focus timer with cycle tracking, pause-aware accounting,
/// long breaks, auto-transitions, and persistent history recording.
/// </summary>
public sealed class FocusTimerService : IDisposable
{
    private FocusTimerState _state = FocusTimerState.Idle;
    private FocusSessionType _currentSessionType = FocusSessionType.Focus;
    private DateTime? _targetDeadlineUtc;
    private TimeSpan? _remainingOnPause;
    private DispatcherTimer? _completionTimer;
    private readonly Dispatcher _dispatcher;

    // Time accounting fields
    private DateTime? _sessionStartUtc;
    private DateTime? _lastActiveSegmentStartUtc;
    private DateTime? _lastPauseStartUtc;
    private double _accumulatedActiveSeconds = 0;
    private double _accumulatedPausedSeconds = 0;

    public int FocusDurationMinutes { get; set; } = 25;
    public int BreakDurationMinutes { get; set; } = 5;
    public int LongBreakDurationMinutes { get; set; } = 15;
    public int LongBreakInterval { get; set; } = 4;
    public int CompletedSessionsInCycle { get; set; } = 0;

    public bool AutoStartBreaks { get; set; } = false;
    public bool AutoStartFocus { get; set; } = false;

    public FocusHistoryService? HistoryService { get; set; }

    public FocusTimerState State => _state;
    public FocusSessionType CurrentSessionType => _currentSessionType;
    public bool IsActive => _state != FocusTimerState.Idle;
    public bool IsPaused => _state is FocusTimerState.FocusPaused or FocusTimerState.BreakPaused;
    public bool IsBreak => _state is FocusTimerState.Break or FocusTimerState.BreakPaused;
    public bool IsLongBreak => IsBreak && _currentSessionType == FocusSessionType.LongBreak;

    public DateTime? TargetDeadlineUtc => _targetDeadlineUtc;

    /// <summary>Optional associated task identifier for the active focus session.</summary>
    public string? ActiveTaskId { get; set; }

    /// <summary>Optional associated task title for display.</summary>
    public string? ActiveTaskTitle { get; set; }

    /// <summary>Optional associated task project name.</summary>
    public string? ActiveTaskProject { get; set; }

    public event Action<FocusTimerState>? StateChanged;
    public event Action? FocusCompleted;
    public event Action? BreakCompleted;
    public event Action<FocusSessionRecord>? SessionRecorded;

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

    /// <summary>Elapsed active seconds in current session.</summary>
    public double CurrentActiveElapsedSeconds
    {
        get
        {
            double extra = 0;
            if ((_state == FocusTimerState.Focusing || _state == FocusTimerState.Break) && _lastActiveSegmentStartUtc.HasValue)
            {
                extra = (DateTime.UtcNow - _lastActiveSegmentStartUtc.Value).TotalSeconds;
            }
            return _accumulatedActiveSeconds + Math.Max(0, extra);
        }
    }

    /// <summary>Start a new focus session, optionally associated with a task.</summary>
    public void StartFocus(int? customMinutes = null, string? taskId = null, string? taskTitle = null, string? project = null)
    {
        StopCompletionTimer();
        int mins = Math.Max(1, customMinutes ?? FocusDurationMinutes);
        var duration = TimeSpan.FromMinutes(mins);

        _targetDeadlineUtc = DateTime.UtcNow + duration;
        _remainingOnPause = null;
        _state = FocusTimerState.Focusing;
        _currentSessionType = FocusSessionType.Focus;

        _sessionStartUtc = DateTime.UtcNow;
        _lastActiveSegmentStartUtc = DateTime.UtcNow;
        _lastPauseStartUtc = null;
        _accumulatedActiveSeconds = 0;
        _accumulatedPausedSeconds = 0;

        ActiveTaskId = taskId;
        ActiveTaskTitle = taskTitle;
        ActiveTaskProject = project;

        ScheduleCompletion(duration);
        StateChanged?.Invoke(_state);
        DebugLog.Write($"[FocusTimer] Focus session started for {mins}m (deadline: {_targetDeadlineUtc:O}, task: {taskTitle ?? "none"})");
    }

    /// <summary>Pause the active session.</summary>
    public void Pause()
    {
        if (_state == FocusTimerState.Focusing)
        {
            StopCompletionTimer();
            if (_lastActiveSegmentStartUtc.HasValue)
            {
                _accumulatedActiveSeconds += (DateTime.UtcNow - _lastActiveSegmentStartUtc.Value).TotalSeconds;
                _lastActiveSegmentStartUtc = null;
            }
            _lastPauseStartUtc = DateTime.UtcNow;

            _remainingOnPause = RemainingTime;
            _targetDeadlineUtc = null;
            _state = FocusTimerState.FocusPaused;
            StateChanged?.Invoke(_state);
            DebugLog.Write($"[FocusTimer] Focus paused. Remaining: {FormattedRemaining}");
        }
        else if (_state == FocusTimerState.Break)
        {
            StopCompletionTimer();
            if (_lastActiveSegmentStartUtc.HasValue)
            {
                _accumulatedActiveSeconds += (DateTime.UtcNow - _lastActiveSegmentStartUtc.Value).TotalSeconds;
                _lastActiveSegmentStartUtc = null;
            }
            _lastPauseStartUtc = DateTime.UtcNow;

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
            if (_lastPauseStartUtc.HasValue)
            {
                _accumulatedPausedSeconds += (DateTime.UtcNow - _lastPauseStartUtc.Value).TotalSeconds;
                _lastPauseStartUtc = null;
            }
            _lastActiveSegmentStartUtc = DateTime.UtcNow;

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
            if (_lastPauseStartUtc.HasValue)
            {
                _accumulatedPausedSeconds += (DateTime.UtcNow - _lastPauseStartUtc.Value).TotalSeconds;
                _lastPauseStartUtc = null;
            }
            _lastActiveSegmentStartUtc = DateTime.UtcNow;

            int fallbackMins = _currentSessionType == FocusSessionType.LongBreak ? LongBreakDurationMinutes : BreakDurationMinutes;
            var rem = _remainingOnPause ?? TimeSpan.FromMinutes(fallbackMins);
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
            StartBreak(_currentSessionType == FocusSessionType.LongBreak);
        }
        else
        {
            StartFocus(FocusDurationMinutes, ActiveTaskId, ActiveTaskTitle, ActiveTaskProject);
        }
    }

    /// <summary>Cancel any active timer and return to Idle.</summary>
    public void Cancel()
    {
        StopCompletionTimer();

        // If it was running for at least 10 active seconds, record as Cancelled / Interrupted
        double activeSec = CurrentActiveElapsedSeconds;
        if (_state != FocusTimerState.Idle && activeSec >= 10 && _sessionStartUtc.HasValue)
        {
            RecordCurrentSession(FocusSessionStatus.Cancelled, activeSec);
        }

        _state = FocusTimerState.Idle;
        _targetDeadlineUtc = null;
        _remainingOnPause = null;
        _sessionStartUtc = null;
        _lastActiveSegmentStartUtc = null;
        _lastPauseStartUtc = null;
        ActiveTaskId = null;
        ActiveTaskTitle = null;
        ActiveTaskProject = null;
        StateChanged?.Invoke(_state);
        DebugLog.Write("[FocusTimer] Focus timer canceled.");
    }

    /// <summary>Clears the associated active task without stopping the timer.</summary>
    public void ClearActiveTask()
    {
        ActiveTaskId = null;
        ActiveTaskTitle = null;
        ActiveTaskProject = null;
        StateChanged?.Invoke(_state);
    }

    /// <summary>Start a break session (either short or long).</summary>
    public void StartBreak(bool isLongBreak = false, int? customMinutes = null)
    {
        StopCompletionTimer();
        _currentSessionType = isLongBreak ? FocusSessionType.LongBreak : FocusSessionType.ShortBreak;
        int mins = Math.Max(1, customMinutes ?? (isLongBreak ? LongBreakDurationMinutes : BreakDurationMinutes));
        var duration = TimeSpan.FromMinutes(mins);

        _targetDeadlineUtc = DateTime.UtcNow + duration;
        _remainingOnPause = null;
        _state = FocusTimerState.Break;

        _sessionStartUtc = DateTime.UtcNow;
        _lastActiveSegmentStartUtc = DateTime.UtcNow;
        _lastPauseStartUtc = null;
        _accumulatedActiveSeconds = 0;
        _accumulatedPausedSeconds = 0;

        ScheduleCompletion(duration);
        StateChanged?.Invoke(_state);
        DebugLog.Write($"[FocusTimer] {(_currentSessionType == FocusSessionType.LongBreak ? "Long" : "Short")} break started for {mins}m (deadline: {_targetDeadlineUtc:O})");
    }

    public void StartBreak(int? customMinutes) => StartBreak(false, customMinutes);

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
            double activeSec = CurrentActiveElapsedSeconds;
            RecordCurrentSession(FocusSessionStatus.Completed, activeSec);

            CompletedSessionsInCycle++;

            _state = FocusTimerState.Idle;
            _targetDeadlineUtc = null;
            _remainingOnPause = null;
            StateChanged?.Invoke(_state);
            FocusCompleted?.Invoke();

            // Auto-transition to break if configured
            if (AutoStartBreaks)
            {
                bool triggerLong = (LongBreakInterval > 0 && CompletedSessionsInCycle % LongBreakInterval == 0);
                StartBreak(triggerLong);
            }
        }
        else if (_state == FocusTimerState.Break)
        {
            DebugLog.Write("[FocusTimer] Break session deadline reached!");
            double activeSec = CurrentActiveElapsedSeconds;
            RecordCurrentSession(FocusSessionStatus.Completed, activeSec);

            _state = FocusTimerState.Idle;
            _targetDeadlineUtc = null;
            _remainingOnPause = null;
            StateChanged?.Invoke(_state);
            BreakCompleted?.Invoke();

            // Auto-transition to focus if configured
            if (AutoStartFocus)
            {
                StartFocus();
            }
        }
    }

    private void RecordCurrentSession(FocusSessionStatus status, double activeSec)
    {
        if (!_sessionStartUtc.HasValue) return;

        double pausedSec = _accumulatedPausedSeconds;
        if (_lastPauseStartUtc.HasValue)
        {
            pausedSec += (DateTime.UtcNow - _lastPauseStartUtc.Value).TotalSeconds;
        }

        int targetMins = _currentSessionType switch
        {
            FocusSessionType.LongBreak => LongBreakDurationMinutes,
            FocusSessionType.ShortBreak => BreakDurationMinutes,
            _ => FocusDurationMinutes
        };

        var record = new FocusSessionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            StartUtc = _sessionStartUtc.Value,
            EndUtc = DateTime.UtcNow,
            SessionType = _currentSessionType,
            TargetMinutes = targetMins,
            ActualSeconds = activeSec,
            PausedSeconds = pausedSec,
            Status = status,
            TaskId = ActiveTaskId,
            TaskTitle = ActiveTaskTitle,
            Project = ActiveTaskProject
        };

        HistoryService?.RecordSession(record);
        SessionRecorded?.Invoke(record);
    }

    /// <summary>Save timer state to AppSettings for persistence across restarts.</summary>
    public void SaveState(AppSettings settings)
    {
        settings.FocusDurationMinutes = FocusDurationMinutes;
        settings.BreakDurationMinutes = BreakDurationMinutes;
        settings.LongBreakDurationMinutes = LongBreakDurationMinutes;
        settings.LongBreakInterval = LongBreakInterval;
        settings.FocusCycleCompletedSessions = CompletedSessionsInCycle;
        settings.AutoStartBreaks = AutoStartBreaks;
        settings.AutoStartFocus = AutoStartFocus;
        settings.FocusTimerActive = IsActive;
        settings.FocusTimerState = _state.ToString();
        settings.FocusTimerSessionType = _currentSessionType.ToString();
        settings.FocusTimerDeadlineUtc = _targetDeadlineUtc;
        settings.FocusTimerRemainingSeconds = RemainingTime.TotalSeconds;
        settings.FocusActiveTaskId = ActiveTaskId;
    }

    /// <summary>Restore timer state from AppSettings on startup.</summary>
    public void LoadState(AppSettings settings)
    {
        FocusDurationMinutes = settings.FocusDurationMinutes > 0 ? settings.FocusDurationMinutes : 25;
        BreakDurationMinutes = settings.BreakDurationMinutes > 0 ? settings.BreakDurationMinutes : 5;
        LongBreakDurationMinutes = settings.LongBreakDurationMinutes > 0 ? settings.LongBreakDurationMinutes : 15;
        LongBreakInterval = settings.LongBreakInterval > 0 ? settings.LongBreakInterval : 4;
        CompletedSessionsInCycle = Math.Max(0, settings.FocusCycleCompletedSessions);
        AutoStartBreaks = settings.AutoStartBreaks;
        AutoStartFocus = settings.AutoStartFocus;
        ActiveTaskId = settings.FocusActiveTaskId;

        if (Enum.TryParse<FocusSessionType>(settings.FocusTimerSessionType, out var savedType))
        {
            _currentSessionType = savedType;
        }

        if (!settings.FocusTimerActive)
        {
            Cancel();
            return;
        }

        if (settings.FocusTimerState == nameof(FocusTimerState.FocusPaused))
        {
            _state = FocusTimerState.FocusPaused;
            _currentSessionType = FocusSessionType.Focus;
            _remainingOnPause = TimeSpan.FromSeconds(Math.Max(0, settings.FocusTimerRemainingSeconds));
            _targetDeadlineUtc = null;
            _sessionStartUtc = DateTime.UtcNow;
            StateChanged?.Invoke(_state);
            return;
        }

        if (settings.FocusTimerState == nameof(FocusTimerState.BreakPaused))
        {
            _state = FocusTimerState.BreakPaused;
            _remainingOnPause = TimeSpan.FromSeconds(Math.Max(0, settings.FocusTimerRemainingSeconds));
            _targetDeadlineUtc = null;
            _sessionStartUtc = DateTime.UtcNow;
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
                _sessionStartUtc = DateTime.UtcNow;
                _lastActiveSegmentStartUtc = DateTime.UtcNow;
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
