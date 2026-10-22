using System.IO;
using System.Text.Json;

namespace DesktopPet.Services;

/// <summary>
/// Local-first task management and scheduling persistence service.
/// Tasks, recurring schedules, and daily routines are stored exclusively on disk
/// in %AppData%\Neku\tasks.json using atomic writes and schema validation.
/// No network calls, telemetry, or server syncing.
/// </summary>
public sealed class TaskService
{
    private readonly string _filePath;
    private readonly List<TaskItem> _tasks = new();
    private readonly List<RecurringSchedule> _schedules = new();
    private readonly List<DailyRoutine> _routines = new();
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IReadOnlyList<TaskItem> Tasks
    {
        get
        {
            lock (_lock)
            {
                return _tasks.ToList();
            }
        }
    }

    public IReadOnlyList<RecurringSchedule> Schedules
    {
        get
        {
            lock (_lock)
            {
                return _schedules.ToList();
            }
        }
    }

    public IReadOnlyList<DailyRoutine> Routines
    {
        get
        {
            lock (_lock)
            {
                return _routines.ToList();
            }
        }
    }

    public event Action<TaskItem>? TaskCompleted;
    public event Action<TaskItem>? TaskReopened;
    public event Action<TaskItem>? TaskDeleted;
    public event Action? TasksChanged;
    public event Action? SchedulesChanged;
    public event Action? RoutinesChanged;
    public event Action<DailyRoutine>? RoutineCompleted;

    public TaskService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "tasks.json");
        }

        Load();
    }

    public string? LastCorruptBackupPath { get; private set; }

    public void Load()
    {
        lock (_lock)
        {
            _tasks.Clear();
            _schedules.Clear();
            _routines.Clear();

            // Check if primary file is missing but an orphaned temp file exists from an interrupted move
            if (!File.Exists(_filePath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(_filePath)!;
                    string baseName = Path.GetFileName(_filePath);
                    if (Directory.Exists(dir))
                    {
                        var candidates = Directory.GetFiles(dir, baseName + ".tmp.*")
                                                  .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                                  .ToList();
                        if (candidates.Count > 0 && new FileInfo(candidates[0]).Length > 0)
                        {
                            File.Move(candidates[0], _filePath, overwrite: true);
                            DebugLog.Write($"[TaskService] Recovered tasks file from interrupted atomic write: {candidates[0]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[TaskService] Error recovering temp file: {ex.Message}");
                }
            }

            if (!File.Exists(_filePath))
            {
                return;
            }

            bool parseSuccess = false;
            try
            {
                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                // 1. Try modern versioned TaskDocument format (v3/v2)
                try
                {
                    var doc = JsonSerializer.Deserialize<TaskDocument>(json, JsonOpts);
                    if (doc?.Tasks != null)
                    {
                        foreach (var task in doc.Tasks)
                        {
                            ValidateAndAdd(task);
                        }

                        if (doc.Schedules != null)
                        {
                            foreach (var schedule in doc.Schedules)
                            {
                                ValidateAndAddSchedule(schedule);
                            }
                        }

                        if (doc.Routines != null)
                        {
                            foreach (var routine in doc.Routines)
                            {
                                ValidateAndAddRoutine(routine);
                            }
                        }

                        parseSuccess = true;
                        ReconcileRecurringSchedulesCore();
                        return;
                    }
                }
                catch
                {
                    // Fallback to older/raw array format
                }

                // 2. Try raw List<TaskItem> format (v1.0.1 legacy)
                try
                {
                    var rawList = JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOpts);
                    if (rawList != null)
                    {
                        foreach (var task in rawList)
                        {
                            ValidateAndAdd(task);
                        }
                        parseSuccess = true;
                        ReconcileRecurringSchedulesCore();
                        return;
                    }
                }
                catch
                {
                }

                if (!parseSuccess)
                {
                    BackupCorruptFile();
                    _tasks.Clear();
                    _schedules.Clear();
                    _routines.Clear();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[TaskService] Failed to read tasks file: {ex.Message}");
                BackupCorruptFile();
                _tasks.Clear();
                _schedules.Clear();
                _routines.Clear();
            }
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(_filePath) && new FileInfo(_filePath).Length > 0)
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string backup = _filePath + ".corrupt." + timestamp;
                if (File.Exists(backup))
                {
                    backup += "_" + Guid.NewGuid().ToString("N")[..6];
                }
                File.Copy(_filePath, backup, overwrite: true);
                LastCorruptBackupPath = backup;
                DebugLog.Write($"[TaskService] Preserved malformed tasks file to: {backup}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[TaskService] Failed to backup malformed tasks file: {ex.Message}");
        }
    }

    private void ValidateAndAdd(TaskItem? task)
    {
        if (task == null) return;

        if (string.IsNullOrWhiteSpace(task.Id))
        {
            task.Id = Guid.NewGuid().ToString("N");
        }

        task.Title ??= "";
        task.Description ??= "";
        task.Project ??= "";
        task.Tags ??= new List<string>();
        task.Subtasks ??= new List<SubtaskItem>();

        if (task.EstimatedMinutes < 0) task.EstimatedMinutes = 0;
        if (task.CompletedPomodoros < 0) task.CompletedPomodoros = 0;
        if (task.TotalFocusMinutes < 0) task.TotalFocusMinutes = 0;

        // Backward compatibility sync between IsCompleted and Status
        if (task.IsCompleted && task.Status != TaskItemStatus.Completed)
        {
            task.Status = TaskItemStatus.Completed;
        }
        else if (!task.IsCompleted && task.Status == TaskItemStatus.Completed)
        {
            task.Status = TaskItemStatus.Todo;
        }

        if (task.ReminderAt.HasValue && task.ReminderAt.Value.Kind != DateTimeKind.Utc)
        {
            task.ReminderAt = task.ReminderAt.Value.ToUniversalTime();
        }

        _tasks.Add(task);
    }

    private void ValidateAndAddSchedule(RecurringSchedule? schedule)
    {
        if (schedule == null) return;

        if (string.IsNullOrWhiteSpace(schedule.Id))
        {
            schedule.Id = Guid.NewGuid().ToString("N");
        }

        schedule.Title ??= "New Recurring Task";
        schedule.Description ??= "";
        schedule.Project ??= "";
        schedule.Tags ??= new List<string>();
        schedule.SubtaskTemplates ??= new List<string>();
        schedule.DaysOfWeek ??= new List<DayOfWeek>();
        schedule.DayOfMonth = Math.Clamp(schedule.DayOfMonth, 1, 31);

        if (string.IsNullOrWhiteSpace(schedule.ScheduledTime))
        {
            schedule.ScheduledTime = "09:00";
        }

        if (string.IsNullOrWhiteSpace(schedule.TimeZoneId))
        {
            schedule.TimeZoneId = TimeZoneInfo.Local.Id;
        }

        _schedules.Add(schedule);
    }

    private void ValidateAndAddRoutine(DailyRoutine? routine)
    {
        if (routine == null) return;

        if (string.IsNullOrWhiteSpace(routine.Id))
        {
            routine.Id = Guid.NewGuid().ToString("N");
        }

        routine.Name ??= "New Routine";
        routine.Description ??= "";
        routine.Icon = string.IsNullOrWhiteSpace(routine.Icon) ? "⚡" : routine.Icon.Trim();
        routine.Steps ??= new List<RoutineStep>();

        foreach (var step in routine.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                step.Id = Guid.NewGuid().ToString("N");
            }
            step.Title ??= "";
            step.Notes ??= "";
            if (step.EstimatedMinutes < 0) step.EstimatedMinutes = 0;
        }

        _routines.Add(routine);
    }

    public void Save()
    {
        lock (_lock)
        {
            string? tempPath = null;
            try
            {
                string dir = Path.GetDirectoryName(_filePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var doc = new TaskDocument
                {
                    Version = 3,
                    Tasks = _tasks.ToList(),
                    Schedules = _schedules.ToList(),
                    Routines = _routines.ToList()
                };

                string json = JsonSerializer.Serialize(doc, JsonOpts);

                // Atomic write via temp file + atomic move to prevent corruption on crash
                tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
                tempPath = null;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[TaskService] Failed to save tasks: {ex.Message}");
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }

    public TaskItem Create(
        string title = "New Task",
        string description = "",
        TaskPriority priority = TaskPriority.Normal,
        DateTime? dueDate = null,
        int estimatedMinutes = 0,
        TaskItemStatus status = TaskItemStatus.Todo,
        string project = "",
        IEnumerable<string>? tags = null,
        TaskRecurrenceRule recurrence = TaskRecurrenceRule.None,
        DateTime? reminderAt = null)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title) ? "New Task" : title.Trim(),
            Description = description?.Trim() ?? "",
            Priority = priority,
            DueDate = dueDate,
            ReminderAt = reminderAt,
            EstimatedMinutes = Math.Max(0, estimatedMinutes),
            Status = status,
            IsCompleted = status == TaskItemStatus.Completed,
            IsSkipped = false,
            Project = project?.Trim() ?? "",
            Tags = tags?.Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList() ?? new List<string>(),
            RecurrenceRule = recurrence,
            CompletedPomodoros = 0,
            TotalFocusMinutes = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _tasks.Insert(0, task);
        }

        Save();
        TasksChanged?.Invoke();
        return task;
    }

    public bool Update(string id, Action<TaskItem> updateAction)
    {
        TaskItem? task;
        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;

            var oldRule = task.RecurrenceRule;
            updateAction(task);
            task.UpdatedAt = DateTime.UtcNow;

            // Maintain schedule as the authoritative source of truth
            if (task.RecurringScheduleId != null && task.RecurrenceRule != oldRule)
            {
                var schedule = _schedules.FirstOrDefault(s => s.Id == task.RecurringScheduleId);
                if (schedule != null)
                {
                    if (task.RecurrenceRule == TaskRecurrenceRule.None)
                    {
                        task.RecurringScheduleId = null;
                    }
                    else
                    {
                        schedule.RecurrenceRule = task.RecurrenceRule;
                        schedule.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
        }

        Save();
        TasksChanged?.Invoke();
        return true;
    }

    public bool UpdateOccurrenceRecurrence(string taskId, TaskRecurrenceRule newRule, bool updateEntireSchedule)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;

            if (task.RecurringScheduleId != null)
            {
                var schedule = _schedules.FirstOrDefault(s => s.Id == task.RecurringScheduleId);
                if (updateEntireSchedule)
                {
                    task.RecurrenceRule = newRule;
                    task.UpdatedAt = DateTime.UtcNow;
                    if (schedule != null)
                    {
                        schedule.RecurrenceRule = newRule;
                        schedule.UpdatedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    // Scope: this occurrence only. Cleanly detach from schedule as a standalone task.
                    task.RecurringScheduleId = null;
                    task.RecurrenceRule = newRule;
                    task.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                task.RecurrenceRule = newRule;
                task.UpdatedAt = DateTime.UtcNow;
            }
        }

        Save();
        TasksChanged?.Invoke();
        SchedulesChanged?.Invoke();
        return true;
    }

    public bool SetReminder(string id, DateTime? reminderAt)
    {
        return Update(id, t => t.ReminderAt = reminderAt);
    }

    public bool ToggleComplete(string id)
    {
        TaskItem? task;
        bool newState;

        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;

            task.IsCompleted = !task.IsCompleted;
            if (task.IsCompleted)
            {
                task.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                task.CompletedAt = null;
                task.IsSkipped = false;
            }
            task.UpdatedAt = DateTime.UtcNow;
            newState = task.IsCompleted;

            if (newState)
            {
                if (task.RecurringScheduleId != null)
                {
                    ReconcileRecurringSchedulesCore();
                }
                else if (task.RecurrenceRule != TaskRecurrenceRule.None)
                {
                    SpawnNextRecurringTask(task);
                }
            }
        }

        Save();
        TasksChanged?.Invoke();

        if (newState)
        {
            TaskCompleted?.Invoke(task);
        }
        else
        {
            TaskReopened?.Invoke(task);
        }

        return newState;
    }

    public bool Complete(string id)
    {
        TaskItem? task;

        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null || task.IsCompleted) return false;

            task.IsCompleted = true;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            if (task.RecurringScheduleId != null)
            {
                ReconcileRecurringSchedulesCore();
            }
            else if (task.RecurrenceRule != TaskRecurrenceRule.None)
            {
                SpawnNextRecurringTask(task);
            }
        }

        Save();
        TasksChanged?.Invoke();
        TaskCompleted?.Invoke(task);
        return true;
    }

    public bool Skip(string id)
    {
        TaskItem? task;

        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null || task.IsCompleted) return false;

            task.IsSkipped = true;
            task.IsCompleted = true;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            if (task.RecurringScheduleId != null)
            {
                ReconcileRecurringSchedulesCore();
            }
            else if (task.RecurrenceRule != TaskRecurrenceRule.None)
            {
                SpawnNextRecurringTask(task);
            }
        }

        Save();
        TasksChanged?.Invoke();
        TaskCompleted?.Invoke(task);
        return true;
    }

    private TaskItem SpawnNextRecurringTask(TaskItem completedTask)
    {
        var nextDue = TaskItem.CalculateNextRecurrenceDate(completedTask.DueDate ?? DateTime.Today, completedTask.RecurrenceRule);
        var rule = completedTask.RecurrenceRule;

        // Strip recurrence from the completed task instance to ensure idempotence
        completedTask.RecurrenceRule = TaskRecurrenceRule.None;

        // Preserve reminder time-of-day for the next occurrence if present
        DateTime? nextReminder = null;
        if (completedTask.ReminderAt.HasValue)
        {
            var localReminder = completedTask.ReminderAt.Value.ToLocalTime();
            var nextReminderLocal = nextDue.Date.Add(localReminder.TimeOfDay);
            nextReminder = nextReminderLocal.ToUniversalTime();
        }

        var nextTask = new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = completedTask.Title,
            Description = completedTask.Description,
            Priority = completedTask.Priority,
            DueDate = nextDue,
            ReminderAt = nextReminder,
            EstimatedMinutes = completedTask.EstimatedMinutes,
            Status = TaskItemStatus.Todo,
            IsCompleted = false,
            IsSkipped = false,
            Project = completedTask.Project,
            Tags = completedTask.Tags.ToList(),
            RecurrenceRule = rule,
            RecurringScheduleId = completedTask.RecurringScheduleId,
            Subtasks = completedTask.Subtasks.Select(s => new SubtaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = s.Title,
                IsCompleted = false
            }).ToList(),
            CompletedPomodoros = 0,
            TotalFocusMinutes = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _tasks.Insert(0, nextTask);
        return nextTask;
    }

    public bool Reopen(string id)
    {
        TaskItem? task;
        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null || !task.IsCompleted) return false;

            task.IsCompleted = false;
            task.IsSkipped = false;
            task.CompletedAt = null;
            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
        TaskReopened?.Invoke(task);
        return true;
    }

    public bool Delete(string id)
    {
        TaskItem? task;
        lock (_lock)
        {
            int index = _tasks.FindIndex(t => t.Id == id);
            if (index < 0) return false;

            task = _tasks[index];
            _tasks.RemoveAt(index);
        }

        Save();
        TasksChanged?.Invoke();
        TaskDeleted?.Invoke(task);
        return true;
    }

    public int ClearCompleted()
    {
        int removedCount;
        lock (_lock)
        {
            removedCount = _tasks.RemoveAll(t => t.IsCompleted);
        }

        if (removedCount > 0)
        {
            Save();
            TasksChanged?.Invoke();
        }
        return removedCount;
    }

    public bool RecordFocusCompleted(string id, int minutes = 25)
    {
        TaskItem? task;
        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;

            task.CompletedPomodoros++;
            task.TotalFocusMinutes += Math.Max(0, minutes);
            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
        return true;
    }

    public bool AddSubtask(string taskId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;

            task.Subtasks.Add(new SubtaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = title.Trim(),
                IsCompleted = false
            });
            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
        return true;
    }

    public bool ToggleSubtask(string taskId, string subtaskId)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;

            var subtask = task.Subtasks.FirstOrDefault(s => s.Id == subtaskId);
            if (subtask == null) return false;

            subtask.IsCompleted = !subtask.IsCompleted;
            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
        return true;
    }

    public bool DeleteSubtask(string taskId, string subtaskId)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;

            int removed = task.Subtasks.RemoveAll(s => s.Id == subtaskId);
            if (removed == 0) return false;

            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
        return true;
    }

    public TaskItem? Get(string id)
    {
        lock (_lock)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }
    }

    public IReadOnlyList<string> GetProjects()
    {
        lock (_lock)
        {
            return _tasks.Select(t => t.Project.Trim())
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(p => p)
                         .ToList();
        }
    }

    public IReadOnlyList<string> GetTags()
    {
        lock (_lock)
        {
            return _tasks.SelectMany(t => t.Tags)
                         .Select(t => t.Trim())
                         .Where(t => !string.IsNullOrEmpty(t))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(t => t)
                         .ToList();
        }
    }

    // ── RECURRING SCHEDULE MANAGEMENT ──────────────────────────────────────────

    public void ReconcileRecurringSchedules(DateTime? nowLocal = null)
    {
        lock (_lock)
        {
            ReconcileRecurringSchedulesCore(nowLocal);
        }
    }

    private void ReconcileRecurringSchedulesCore(DateTime? nowLocal = null)
    {
        var localNow = nowLocal ?? DateTime.Now;
        var today = localNow.Date;
        bool anyAdded = false;

        foreach (var schedule in _schedules)
        {
            if (schedule.IsPaused) continue;

            // Check if there is an active uncompleted occurrence
            bool hasActiveOccurrence = _tasks.Any(t =>
                !t.IsCompleted && !t.IsSkipped &&
                t.RecurringScheduleId == schedule.Id);

            if (hasActiveOccurrence)
            {
                continue;
            }

            // Determine target occurrence date
            DateTime targetDate;
            if (!schedule.LastOccurrenceDate.HasValue)
            {
                targetDate = RecurrenceEngine.GetFirstOccurrenceDate(schedule, localNow);
            }
            else
            {
                targetDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, schedule.LastOccurrenceDate.Value);
            }

            // Fast-forward if targetDate missed multiple cycles while offline
            while (targetDate < today)
            {
                var next = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, targetDate);
                if (next <= targetDate) break;
                targetDate = next;
            }

            // Advance past any already existing occurrences to ensure idempotency
            while (_tasks.Any(t =>
                t.RecurringScheduleId == schedule.Id &&
                t.OccurrenceDate.HasValue &&
                t.OccurrenceDate.Value.Date == targetDate.Date))
            {
                var next = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, targetDate);
                if (next <= targetDate) break;
                targetDate = next;
            }

            RecurrenceEngine.CalculateOccurrenceDueAndReminder(
                schedule,
                targetDate,
                out var dueLocal,
                out var reminderUtc);

            var occurrence = new TaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = schedule.Title,
                Description = schedule.Description,
                Priority = schedule.Priority,
                DueDate = dueLocal,
                ReminderAt = reminderUtc,
                EstimatedMinutes = schedule.EstimatedMinutes,
                Status = TaskItemStatus.Todo,
                IsCompleted = false,
                IsSkipped = false,
                Project = schedule.Project,
                Tags = schedule.Tags.ToList(),
                RecurrenceRule = schedule.RecurrenceRule,
                RecurringScheduleId = schedule.Id,
                OccurrenceDate = targetDate,
                Subtasks = schedule.SubtaskTemplates.Select(t => new SubtaskItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = t,
                    IsCompleted = false
                }).ToList(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _tasks.Insert(0, occurrence);
            schedule.LastOccurrenceDate = targetDate;
            schedule.UpdatedAt = DateTime.UtcNow;
            anyAdded = true;
        }

        if (anyAdded)
        {
            Save();
            TasksChanged?.Invoke();
        }
    }

    public RecurringSchedule CreateSchedule(
        string title,
        TaskRecurrenceRule rule = TaskRecurrenceRule.Daily,
        string scheduledTime = "09:00",
        IEnumerable<DayOfWeek>? daysOfWeek = null,
        int dayOfMonth = 1,
        int? reminderMinutesBefore = 0,
        string description = "",
        TaskPriority priority = TaskPriority.Normal,
        int estimatedMinutes = 0,
        string project = "",
        IEnumerable<string>? tags = null)
    {
        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title) ? "New Recurring Task" : title.Trim(),
            Description = description?.Trim() ?? "",
            Priority = priority,
            EstimatedMinutes = Math.Max(0, estimatedMinutes),
            Project = project?.Trim() ?? "",
            Tags = tags?.Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList() ?? new List<string>(),
            RecurrenceRule = rule,
            DaysOfWeek = daysOfWeek?.Distinct().ToList() ?? new List<DayOfWeek>(),
            DayOfMonth = Math.Clamp(dayOfMonth, 1, 31),
            ScheduledTime = string.IsNullOrWhiteSpace(scheduledTime) ? "09:00" : scheduledTime.Trim(),
            TimeZoneId = TimeZoneInfo.Local.Id,
            ReminderMinutesBefore = reminderMinutesBefore,
            IsPaused = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _schedules.Add(schedule);
            ReconcileRecurringSchedulesCore();
        }

        Save();
        SchedulesChanged?.Invoke();
        return schedule;
    }

    public bool UpdateSchedule(string id, Action<RecurringSchedule> updateAction)
    {
        RecurringSchedule? schedule;
        lock (_lock)
        {
            schedule = _schedules.FirstOrDefault(s => s.Id == id);
            if (schedule == null) return false;

            updateAction(schedule);
            schedule.UpdatedAt = DateTime.UtcNow;

            // Keep any active uncompleted occurrence synchronized with parent schedule
            var activeOccurrence = _tasks.FirstOrDefault(t => t.RecurringScheduleId == id && !t.IsCompleted && !t.IsSkipped);
            if (activeOccurrence != null)
            {
                activeOccurrence.Title = schedule.Title;
                activeOccurrence.Description = schedule.Description;
                activeOccurrence.Priority = schedule.Priority;
                activeOccurrence.RecurrenceRule = schedule.RecurrenceRule;
                activeOccurrence.Project = schedule.Project;
                activeOccurrence.UpdatedAt = DateTime.UtcNow;
            }

            ReconcileRecurringSchedulesCore();
        }

        Save();
        SchedulesChanged?.Invoke();
        return true;
    }

    public bool PauseSchedule(string id)
    {
        lock (_lock)
        {
            var sched = _schedules.FirstOrDefault(s => s.Id == id);
            if (sched == null || sched.IsPaused) return false;
            sched.IsPaused = true;
            sched.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        SchedulesChanged?.Invoke();
        return true;
    }

    public bool ResumeSchedule(string id)
    {
        lock (_lock)
        {
            var sched = _schedules.FirstOrDefault(s => s.Id == id);
            if (sched == null || !sched.IsPaused) return false;
            sched.IsPaused = false;
            sched.UpdatedAt = DateTime.UtcNow;
            ReconcileRecurringSchedulesCore();
        }

        Save();
        SchedulesChanged?.Invoke();
        return true;
    }

    public bool DeleteSchedule(string id)
    {
        lock (_lock)
        {
            int index = _schedules.FindIndex(s => s.Id == id);
            if (index < 0) return false;
            _schedules.RemoveAt(index);

            // Detach active uncompleted occurrences so they become standalone tasks without further recurrence
            foreach (var t in _tasks.Where(t => t.RecurringScheduleId == id && !t.IsCompleted))
            {
                t.RecurringScheduleId = null;
                t.RecurrenceRule = TaskRecurrenceRule.None;
            }
        }

        Save();
        SchedulesChanged?.Invoke();
        TasksChanged?.Invoke();
        return true;
    }

    public RecurringSchedule? GetSchedule(string id)
    {
        lock (_lock)
        {
            return _schedules.FirstOrDefault(s => s.Id == id);
        }
    }

    // ── DAILY ROUTINE MANAGEMENT ───────────────────────────────────────────────

    public DailyRoutine CreateRoutine(
        string name = "New Routine",
        string description = "",
        string icon = "⚡",
        IEnumerable<RoutineStep>? steps = null)
    {
        var routine = new DailyRoutine
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "New Routine" : name.Trim(),
            Description = description?.Trim() ?? "",
            Icon = string.IsNullOrWhiteSpace(icon) ? "⚡" : icon.Trim(),
            Steps = steps?.ToList() ?? new List<RoutineStep>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _routines.Add(routine);
        }

        Save();
        RoutinesChanged?.Invoke();
        return routine;
    }

    public bool UpdateRoutine(string id, Action<DailyRoutine> updateAction)
    {
        DailyRoutine? routine;
        lock (_lock)
        {
            routine = _routines.FirstOrDefault(r => r.Id == id);
            if (routine == null) return false;

            updateAction(routine);
            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }

    public bool DeleteRoutine(string id)
    {
        lock (_lock)
        {
            int index = _routines.FindIndex(r => r.Id == id);
            if (index < 0) return false;
            _routines.RemoveAt(index);
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }

    public DailyRoutine? GetRoutine(string id)
    {
        lock (_lock)
        {
            return _routines.FirstOrDefault(r => r.Id == id);
        }
    }

    public bool StartRoutine(string id)
    {
        lock (_lock)
        {
            var routine = _routines.FirstOrDefault(r => r.Id == id);
            if (routine == null) return false;

            foreach (var step in routine.Steps)
            {
                step.IsCompleted = false;
                step.IsSkipped = false;
                step.CompletedAt = null;
            }

            routine.IsActive = true;
            routine.StartedAt = DateTime.UtcNow;
            routine.CurrentStepIndex = 0;
            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }

    public bool CompleteRoutineStep(string routineId, string stepId)
    {
        DailyRoutine? completedRoutine = null;
        lock (_lock)
        {
            var routine = _routines.FirstOrDefault(r => r.Id == routineId);
            if (routine == null) return false;

            var step = routine.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            step.IsCompleted = true;
            step.CompletedAt = DateTime.UtcNow;

            int nextIdx = routine.Steps.FindIndex(s => !s.IsCompleted && !s.IsSkipped);
            if (nextIdx >= 0)
            {
                routine.CurrentStepIndex = nextIdx;
            }
            else
            {
                routine.IsActive = false;
                routine.LastCompletedAt = DateTime.UtcNow;
                completedRoutine = routine;
            }

            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        if (completedRoutine != null)
        {
            RoutineCompleted?.Invoke(completedRoutine);
        }
        return true;
    }

    public bool SkipRoutineStep(string routineId, string stepId)
    {
        lock (_lock)
        {
            var routine = _routines.FirstOrDefault(r => r.Id == routineId);
            if (routine == null) return false;

            var step = routine.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            step.IsSkipped = true;
            step.CompletedAt = DateTime.UtcNow;

            int nextIdx = routine.Steps.FindIndex(s => !s.IsCompleted && !s.IsSkipped);
            if (nextIdx >= 0)
            {
                routine.CurrentStepIndex = nextIdx;
            }
            else
            {
                routine.IsActive = false;
                routine.LastCompletedAt = DateTime.UtcNow;
            }

            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }

    public bool ResetRoutine(string id)
    {
        lock (_lock)
        {
            var routine = _routines.FirstOrDefault(r => r.Id == id);
            if (routine == null) return false;

            routine.IsActive = false;
            foreach (var step in routine.Steps)
            {
                step.IsCompleted = false;
                step.IsSkipped = false;
                step.CompletedAt = null;
            }
            routine.CurrentStepIndex = 0;
            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }

    public bool ReorderRoutineSteps(string routineId, int oldIndex, int newIndex)
    {
        lock (_lock)
        {
            var routine = _routines.FirstOrDefault(r => r.Id == routineId);
            if (routine == null) return false;
            if (oldIndex < 0 || oldIndex >= routine.Steps.Count ||
                newIndex < 0 || newIndex >= routine.Steps.Count ||
                oldIndex == newIndex)
            {
                return false;
            }

            var item = routine.Steps[oldIndex];
            routine.Steps.RemoveAt(oldIndex);
            routine.Steps.Insert(newIndex, item);
            routine.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        RoutinesChanged?.Invoke();
        return true;
    }
}
