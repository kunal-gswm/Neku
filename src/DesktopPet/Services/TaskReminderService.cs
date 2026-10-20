using System.Windows.Threading;

namespace DesktopPet.Services;

/// <summary>
/// Authoritative local scheduler and notification manager for scheduled task reminders.
/// Features missed-reminder flood prevention, quiet-hours suppression, snooze scheduling,
/// and automatic reconciliation with task completion and deletion.
/// </summary>
public sealed class TaskReminderService : IDisposable
{
    private readonly TaskService _taskService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Func<DateTime> _utcNowProvider;
    private readonly DispatcherTimer _timer;

    private readonly HashSet<string> _activeAlertTaskIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suppressedTaskIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Windows.Window> _activeNotifications = new(StringComparer.OrdinalIgnoreCase);

    public event Action<TaskItem, bool>? ReminderDue;
    public event Action<int, IReadOnlyList<TaskItem>>? ConsolidatedRemindersDue;

    public Func<bool>? IsQuietOrPresenting { get; set; }

    public DateTime UtcNow => _utcNowProvider();

    public TaskReminderService(
        TaskService taskService,
        AppSettings settings,
        Dispatcher? dispatcher = null,
        Func<DateTime>? utcNowProvider = null)
    {
        _taskService = taskService;
        _settings = settings;
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);

        _taskService.TaskCompleted += OnTaskEnded;
        _taskService.TaskDeleted += OnTaskEnded;

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += (_, _) => CheckReminders();
        _timer.Start();

        // Perform initial check on startup for missed/pending reminders
        _dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => CheckReminders());
    }

    private void OnTaskEnded(TaskItem task)
    {
        if (task == null) return;
        DismissReminder(task.Id, persistTask: false);
    }

    public bool IsInQuietHours()
    {
        if (!_settings.EnableQuietHours) return false;

        if (!TimeSpan.TryParse(_settings.QuietHoursStart, out var start) ||
            !TimeSpan.TryParse(_settings.QuietHoursEnd, out var end))
        {
            return false;
        }

        var nowLocal = UtcNow.ToLocalTime().TimeOfDay;

        if (start <= end)
        {
            // Standard range within same calendar day (e.g. 09:00 to 17:00)
            return nowLocal >= start && nowLocal < end;
        }
        else
        {
            // Overnight range spanning midnight (e.g. 22:00 to 08:00)
            return nowLocal >= start || nowLocal < end;
        }
    }

    public bool IsSuppressed()
    {
        if (_settings.EnableQuietMode) return true;
        if (IsQuietOrPresenting?.Invoke() == true) return true;
        if (IsInQuietHours()) return true;
        return false;
    }

    public void CheckReminders()
    {
        if (!_settings.EnableScheduledReminders) return;

        var now = UtcNow;
        bool suppressed = IsSuppressed();

        var pendingTasks = _taskService.Tasks
            .Where(t => !t.IsCompleted && t.ReminderAt.HasValue && t.ReminderAt.Value <= now)
            .ToList();

        if (suppressed)
        {
            foreach (var t in pendingTasks)
            {
                _suppressedTaskIds.Add(t.Id);
            }
            return;
        }

        // Handle drain of any previously suppressed reminders
        _suppressedTaskIds.Clear();

        var dueList = new List<TaskItem>();

        foreach (var task in pendingTasks)
        {
            if (_activeAlertTaskIds.Contains(task.Id))
            {
                continue;
            }

            var elapsed = now - task.ReminderAt!.Value;

            // Stale Policy: If the reminder expired more than 24 hours ago, do not pop up intrusive alerts.
            if (elapsed > TimeSpan.FromHours(24))
            {
                // Clear the ancient reminder timestamp silently so user is not spammed
                _taskService.SetReminder(task.Id, null);
                continue;
            }

            dueList.Add(task);
        }

        if (dueList.Count == 0) return;

        // Flood Policy: If more than 3 reminders become due at once (e.g. after sleep or quiet hours),
        // present a single consolidated summary notification instead of cascading popups.
        if (dueList.Count > 3)
        {
            foreach (var t in dueList)
            {
                _activeAlertTaskIds.Add(t.Id);
            }
            ConsolidatedRemindersDue?.Invoke(dueList.Count, dueList);
        }
        else
        {
            foreach (var task in dueList)
            {
                _activeAlertTaskIds.Add(task.Id);
                bool isMissed = (now - task.ReminderAt!.Value) > TimeSpan.FromMinutes(5);
                ReminderDue?.Invoke(task, isMissed);
            }
        }
    }

    public bool CompleteTask(string taskId)
    {
        _activeAlertTaskIds.Remove(taskId);
        CloseActiveNotification(taskId);
        return _taskService.Complete(taskId);
    }

    public bool SnoozeReminder(string taskId, int minutes)
    {
        if (minutes <= 0) minutes = Math.Max(1, _settings.DefaultSnoozeMinutes);

        _activeAlertTaskIds.Remove(taskId);
        CloseActiveNotification(taskId);

        var nextReminderUtc = UtcNow.AddMinutes(minutes);
        return _taskService.SetReminder(taskId, nextReminderUtc);
    }

    public bool DismissReminder(string taskId, bool persistTask = true)
    {
        _activeAlertTaskIds.Remove(taskId);
        CloseActiveNotification(taskId);

        if (persistTask)
        {
            return _taskService.SetReminder(taskId, null);
        }
        return true;
    }

    public bool SkipTask(string taskId)
    {
        _activeAlertTaskIds.Remove(taskId);
        CloseActiveNotification(taskId);
        return _taskService.Skip(taskId);
    }

    public void RegisterActiveNotification(string taskId, System.Windows.Window window)
    {
        if (string.IsNullOrEmpty(taskId) || window == null) return;
        _activeNotifications[taskId] = window;
        window.Closed += (_, _) =>
        {
            if (_activeNotifications.TryGetValue(taskId, out var w) && w == window)
            {
                _activeNotifications.Remove(taskId);
                _activeAlertTaskIds.Remove(taskId);
            }
        };
    }

    public void CloseActiveNotification(string taskId)
    {
        if (_activeNotifications.TryGetValue(taskId, out var window))
        {
            _activeNotifications.Remove(taskId);
            try
            {
                window.Close();
            }
            catch
            {
                // Ignore window close exceptions during teardown
            }
        }
    }

    public void CloseAllNotifications()
    {
        foreach (var window in _activeNotifications.Values.ToList())
        {
            try { window.Close(); } catch { }
        }
        _activeNotifications.Clear();
        _activeAlertTaskIds.Clear();
    }

    public void Dispose()
    {
        _timer.Stop();
        _taskService.TaskCompleted -= OnTaskEnded;
        _taskService.TaskDeleted -= OnTaskEnded;
        CloseAllNotifications();
    }
}
