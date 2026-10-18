using System.Windows.Threading;

namespace DesktopPet.Services;

/// <summary>
/// Configurable background reminder service for task deadlines and breaks.
/// Features duplicate suppression (alerts at most once per task per calendar day),
/// graceful quiet/presentation mode suppression, and clean testability.
/// </summary>
public sealed class ProductivityReminderService : IDisposable
{
    private readonly TaskService _taskService;
    private readonly FocusTimerService _focusTimer;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;

    private readonly HashSet<string> _remindedKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _focusCompletedAtUtc;
    private bool _breakReminderSentForLatestSession = false;

    public event Action<TaskItem, string>? TaskDeadlineReminder;
    public event Action<string>? BreakReminder;

    public Func<bool>? IsQuietOrPresenting { get; set; }

    public ProductivityReminderService(
        TaskService taskService,
        FocusTimerService focusTimer,
        AppSettings settings,
        Dispatcher? dispatcher = null)
    {
        _taskService = taskService;
        _focusTimer = focusTimer;
        _settings = settings;

        _focusTimer.FocusCompleted += OnFocusCompleted;
        _focusTimer.StateChanged += OnTimerStateChanged;

        var disp = dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, disp)
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.ReminderIntervalMinutes > 0 ? 2 : 5))
        };
        _timer.Tick += (_, _) => CheckReminders();
        _timer.Start();
    }

    private void OnFocusCompleted()
    {
        _focusCompletedAtUtc = DateTime.UtcNow;
        _breakReminderSentForLatestSession = false;
    }

    private void OnTimerStateChanged(FocusTimerState state)
    {
        if (state == FocusTimerState.Break || state == FocusTimerState.Focusing)
        {
            _breakReminderSentForLatestSession = true;
        }
    }

    public void CheckReminders()
    {
        // 1. Check suppression conditions
        if (_settings.EnableQuietMode || IsQuietOrPresenting?.Invoke() == true)
        {
            return;
        }

        var today = DateTime.Today;

        // 2. Task Deadline Reminders
        if (_settings.EnableUpcomingTaskReminders || _settings.EnableOverdueTaskReminders)
        {
            var tasks = _taskService.Tasks.Where(t => !t.IsCompleted && t.DueDate.HasValue).ToList();
            foreach (var task in tasks)
            {
                var localDueDate = TaskItem.ToLocalDate(task.DueDate!.Value);

                if (localDueDate < today)
                {
                    if (_settings.EnableOverdueTaskReminders)
                    {
                        string key = $"{task.Id}_{today:yyyyMMdd}_overdue";
                        if (_remindedKeys.Add(key))
                        {
                            TaskDeadlineReminder?.Invoke(task, $"⚠️ Task overdue: \"{task.Title}\"");
                        }
                    }
                }
                else if (localDueDate == today)
                {
                    if (_settings.EnableUpcomingTaskReminders)
                    {
                        string key = $"{task.Id}_{today:yyyyMMdd}_today";
                        if (_remindedKeys.Add(key))
                        {
                            TaskDeadlineReminder?.Invoke(task, $"⏰ Due today: \"{task.Title}\"");
                        }
                    }
                }
            }
        }

        // 3. Break Reminders
        if (_settings.EnableBreakReminders && !_breakReminderSentForLatestSession && _focusCompletedAtUtc.HasValue)
        {
            var elapsedSinceFocus = DateTime.UtcNow - _focusCompletedAtUtc.Value;
            if (elapsedSinceFocus >= TimeSpan.FromMinutes(2) && !_focusTimer.IsActive)
            {
                _breakReminderSentForLatestSession = true;
                BreakReminder?.Invoke("☕ Time for a refreshing break! Stretch and rest your eyes.");
            }
        }
    }

    public void ClearReminderCache()
    {
        _remindedKeys.Clear();
        _breakReminderSentForLatestSession = false;
    }

    public void Dispose()
    {
        _timer.Stop();
        _focusTimer.FocusCompleted -= OnFocusCompleted;
        _focusTimer.StateChanged -= OnTimerStateChanged;
    }
}
