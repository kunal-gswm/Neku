using System.IO;
using System.Text.Json;

namespace DesktopPet.Services;

/// <summary>
/// Local-first task management persistence service. Tasks are stored exclusively on disk
/// in %AppData%\Neku\tasks.json using atomic writes and robust schema validation.
/// No network calls, telemetry, or server syncing.
/// </summary>
public sealed class TaskService
{
    private readonly string _filePath;
    private readonly List<TaskItem> _tasks = new();
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

    public event Action<TaskItem>? TaskCompleted;
    public event Action<TaskItem>? TaskReopened;
    public event Action<TaskItem>? TaskDeleted;
    public event Action? TasksChanged;

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

                // 1. Try modern versioned TaskDocument format
                try
                {
                    var doc = JsonSerializer.Deserialize<TaskDocument>(json, JsonOpts);
                    if (doc?.Tasks != null)
                    {
                        foreach (var task in doc.Tasks)
                        {
                            ValidateAndAdd(task);
                        }
                        parseSuccess = true;
                        return;
                    }
                }
                catch
                {
                    // Fallback to older/raw array format
                }

                // 2. Try raw List<TaskItem> format
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
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[TaskService] Failed to read tasks file: {ex.Message}");
                BackupCorruptFile();
                _tasks.Clear();
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

        if (task.Title == null)
        {
            task.Title = "";
        }

        if (task.Description == null)
        {
            task.Description = "";
        }

        if (task.Project == null)
        {
            task.Project = "";
        }

        task.Tags ??= new List<string>();
        task.Subtasks ??= new List<SubtaskItem>();

        if (task.EstimatedMinutes < 0)
        {
            task.EstimatedMinutes = 0;
        }

        if (task.CompletedPomodoros < 0)
        {
            task.CompletedPomodoros = 0;
        }

        if (task.TotalFocusMinutes < 0)
        {
            task.TotalFocusMinutes = 0;
        }

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
                    Version = 2,
                    Tasks = _tasks.ToList()
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

            updateAction(task);
            task.UpdatedAt = DateTime.UtcNow;
        }

        Save();
        TasksChanged?.Invoke();
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
        TaskItem? recurringSpawn = null;

        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;

            task.IsCompleted = !task.IsCompleted;
            task.CompletedAt = task.IsCompleted ? DateTime.UtcNow : null;
            task.UpdatedAt = DateTime.UtcNow;
            newState = task.IsCompleted;

            if (newState && task.RecurrenceRule != TaskRecurrenceRule.None)
            {
                recurringSpawn = SpawnNextRecurringTask(task);
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
        TaskItem? recurringSpawn = null;

        lock (_lock)
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null || task.IsCompleted) return false;

            task.IsCompleted = true;
            task.CompletedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;

            if (task.RecurrenceRule != TaskRecurrenceRule.None)
            {
                recurringSpawn = SpawnNextRecurringTask(task);
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

        var nextTask = new TaskItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = completedTask.Title,
            Description = completedTask.Description,
            Priority = completedTask.Priority,
            DueDate = nextDue,
            EstimatedMinutes = completedTask.EstimatedMinutes,
            Status = TaskItemStatus.Todo,
            IsCompleted = false,
            Project = completedTask.Project,
            Tags = completedTask.Tags.ToList(),
            RecurrenceRule = rule,
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

    /// <summary>
    /// Records a completed Pomodoro focus session for the specified task without automatically marking it complete.
    /// </summary>
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
}
