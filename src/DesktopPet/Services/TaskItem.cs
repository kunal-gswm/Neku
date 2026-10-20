using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TaskItemStatus
{
    Inbox = 0,
    Todo = 1,
    InProgress = 2,
    Completed = 3
}

public enum TaskRecurrenceRule
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Weekdays = 3,
    Monthly = 4
}

public sealed class SubtaskItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "";
    private bool _isCompleted = false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted
    {
        get => _isCompleted;
        set { if (_isCompleted != value) { _isCompleted = value; OnPropertyChanged(); } }
    }
}

public sealed class TaskItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "New Task";
    private string _description = "";
    private bool _isCompleted = false;
    private TaskItemStatus _status = TaskItemStatus.Todo;
    private TaskPriority _priority = TaskPriority.Normal;
    private DateTime? _dueDate;
    private DateTime? _reminderAt;
    private int _estimatedMinutes = 0;
    private int _completedPomodoros = 0;
    private int _totalFocusMinutes = 0;
    private string _project = "";
    private List<string> _tags = new();
    private List<SubtaskItem> _subtasks = new();
    private TaskRecurrenceRule _recurrenceRule = TaskRecurrenceRule.None;
    private string? _recurringScheduleId;
    private bool _isSkipped = false;
    private DateTime? _occurrenceDate;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime? _completedAt;
    private DateTime _updatedAt = DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                if (_isCompleted && _status != TaskItemStatus.Completed)
                {
                    _status = TaskItemStatus.Completed;
                    OnPropertyChanged(nameof(Status));
                }
                else if (!_isCompleted && _status == TaskItemStatus.Completed)
                {
                    _status = TaskItemStatus.Todo;
                    OnPropertyChanged(nameof(Status));
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOverdue));
                OnPropertyChanged(nameof(IsDueToday));
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                if (_status == TaskItemStatus.Completed && !_isCompleted)
                {
                    _isCompleted = true;
                    OnPropertyChanged(nameof(IsCompleted));
                }
                else if (_status != TaskItemStatus.Completed && _isCompleted)
                {
                    _isCompleted = false;
                    OnPropertyChanged(nameof(IsCompleted));
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOverdue));
                OnPropertyChanged(nameof(IsDueToday));
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    [JsonPropertyName("priority")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskPriority Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriorityDisplay));
            }
        }
    }

    [JsonPropertyName("dueDate")]
    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (_dueDate != value)
            {
                _dueDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOverdue));
                OnPropertyChanged(nameof(IsDueToday));
                OnPropertyChanged(nameof(FormattedDueDate));
            }
        }
    }

    /// <summary>Optional scheduled local reminder timestamp in UTC.</summary>
    [JsonPropertyName("reminderAt")]
    public DateTime? ReminderAt
    {
        get => _reminderAt;
        set
        {
            var normalized = value.HasValue
                ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime())
                : (DateTime?)null;

            if (_reminderAt != normalized)
            {
                _reminderAt = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasReminder));
                OnPropertyChanged(nameof(IsReminderOverdue));
                OnPropertyChanged(nameof(FormattedReminder));
            }
        }
    }

    /// <summary>Estimated duration in minutes (0 = none/unspecified).</summary>
    [JsonPropertyName("estimatedMinutes")]
    public int EstimatedMinutes
    {
        get => _estimatedMinutes;
        set { if (_estimatedMinutes != value) { _estimatedMinutes = value; OnPropertyChanged(); } }
    }

    /// <summary>Number of Pomodoro focus sessions successfully completed on this task.</summary>
    [JsonPropertyName("completedPomodoros")]
    public int CompletedPomodoros
    {
        get => _completedPomodoros;
        set
        {
            if (_completedPomodoros != value)
            {
                _completedPomodoros = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PomodoroDisplay));
            }
        }
    }

    /// <summary>Total minutes spent focusing on this task.</summary>
    [JsonPropertyName("totalFocusMinutes")]
    public int TotalFocusMinutes
    {
        get => _totalFocusMinutes;
        set
        {
            if (_totalFocusMinutes != value)
            {
                _totalFocusMinutes = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Optional category or project name.</summary>
    [JsonPropertyName("project")]
    public string Project
    {
        get => _project;
        set { if (_project != value) { _project = value; OnPropertyChanged(); } }
    }

    /// <summary>Tags or labels associated with the task.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set { _tags = value ?? new List<string>(); OnPropertyChanged(); OnPropertyChanged(nameof(TagsDisplay)); }
    }

    /// <summary>Checklist of subtasks.</summary>
    [JsonPropertyName("subtasks")]
    public List<SubtaskItem> Subtasks
    {
        get => _subtasks;
        set
        {
            _subtasks = value ?? new List<SubtaskItem>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubtasksSummary));
            OnPropertyChanged(nameof(HasSubtasks));
        }
    }

    /// <summary>Recurrence rule (None, Daily, Weekly, Weekdays, Monthly).</summary>
    [JsonPropertyName("recurrenceRule")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskRecurrenceRule RecurrenceRule
    {
        get => _recurrenceRule;
        set
        {
            if (_recurrenceRule != value)
            {
                _recurrenceRule = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecurrenceDisplay));
            }
        }
    }

    /// <summary>Optional identifier linking this occurrence to its originating RecurringSchedule.</summary>
    [JsonPropertyName("recurringScheduleId")]
    public string? RecurringScheduleId
    {
        get => _recurringScheduleId;
        set { if (_recurringScheduleId != value) { _recurringScheduleId = value; OnPropertyChanged(); } }
    }

    /// <summary>Whether this individual recurring occurrence was skipped rather than completed.</summary>
    [JsonPropertyName("isSkipped")]
    public bool IsSkipped
    {
        get => _isSkipped;
        set
        {
            if (_isSkipped != value)
            {
                _isSkipped = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    /// <summary>Local calendar date this occurrence was scheduled for.</summary>
    [JsonPropertyName("occurrenceDate")]
    public DateTime? OccurrenceDate
    {
        get => _occurrenceDate;
        set { if (_occurrenceDate != value) { _occurrenceDate = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { if (_completedAt != value) { _completedAt = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set { if (_updatedAt != value) { _updatedAt = value; OnPropertyChanged(); } }
    }

    /// <summary>Normalizes any DateTime (UTC, Local, or Unspecified) to local calendar date.</summary>
    public static DateTime ToLocalDate(DateTime dt)
    {
        return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime().Date : dt.Date;
    }

    public static DateTime CalculateNextRecurrenceDate(DateTime currentDate, TaskRecurrenceRule rule)
    {
        var baseDate = ToLocalDate(currentDate);
        return rule switch
        {
            TaskRecurrenceRule.Daily => baseDate.AddDays(1),
            TaskRecurrenceRule.Weekdays => RecurrenceEngine.CalculateNextOccurrenceDate(new RecurringSchedule { RecurrenceRule = TaskRecurrenceRule.Weekdays }, baseDate),
            TaskRecurrenceRule.Weekly => baseDate.AddDays(7),
            TaskRecurrenceRule.Monthly => RecurrenceEngine.CalculateNextOccurrenceDate(new RecurringSchedule { RecurrenceRule = TaskRecurrenceRule.Monthly, DayOfMonth = baseDate.Day }, baseDate),
            _ => baseDate
        };
    }

    // Helper properties for UI data binding
    [JsonIgnore]
    public bool HasReminder => ReminderAt.HasValue;

    [JsonIgnore]
    public bool IsReminderOverdue => !IsCompleted && ReminderAt.HasValue && ReminderAt.Value <= DateTime.UtcNow;

    [JsonIgnore]
    public string FormattedReminder
    {
        get
        {
            if (!ReminderAt.HasValue) return "";
            var local = ReminderAt.Value.ToLocalTime();
            var today = DateTime.Today;
            if (local.Date == today)
            {
                return $"Today {local:t}";
            }
            if (local.Date == today.AddDays(1))
            {
                return $"Tomorrow {local:t}";
            }
            return $"{local:MMM d, t}";
        }
    }

    [JsonIgnore]
    public bool IsOverdue => !IsCompleted && DueDate.HasValue && ToLocalDate(DueDate.Value) < DateTime.Today;

    [JsonIgnore]
    public bool IsDueToday => !IsCompleted && DueDate.HasValue && ToLocalDate(DueDate.Value) == DateTime.Today;

    [JsonIgnore]
    public string FormattedDueDate => DueDate.HasValue ? ToLocalDate(DueDate.Value).ToString("MMM d") : "";

    [JsonIgnore]
    public string PriorityDisplay => Priority switch
    {
        TaskPriority.Urgent => "Urgent 🔥",
        TaskPriority.High => "High",
        TaskPriority.Low => "Low",
        _ => "Normal"
    };

    [JsonIgnore]
    public string PomodoroDisplay => CompletedPomodoros > 0 ? $"🍅 {CompletedPomodoros}" : "";

    [JsonIgnore]
    public string StatusDisplay => IsSkipped ? "Skipped ⏭️" : (IsCompleted ? "Completed" : (IsOverdue ? "Overdue" : (IsDueToday ? "Due Today" : _status.ToString())));

    [JsonIgnore]
    public string SubtasksSummary => Subtasks.Count > 0 ? $"{Subtasks.Count(s => s.IsCompleted)}/{Subtasks.Count} subtasks" : "";

    [JsonIgnore]
    public bool HasSubtasks => Subtasks.Count > 0;

    [JsonIgnore]
    public string RecurrenceDisplay => RecurrenceRule switch
    {
        TaskRecurrenceRule.Daily => "🔁 Daily",
        TaskRecurrenceRule.Weekdays => "🔁 Weekdays",
        TaskRecurrenceRule.Weekly => "🔁 Weekly",
        TaskRecurrenceRule.Monthly => "🔁 Monthly",
        _ => ""
    };

    [JsonIgnore]
    public string TagsDisplay => Tags.Count > 0 ? string.Join(", ", Tags) : "";
}

public sealed class TaskDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("tasks")]
    public List<TaskItem> Tasks { get; set; } = new();

    [JsonPropertyName("schedules")]
    public List<RecurringSchedule> Schedules { get; set; } = new();

    [JsonPropertyName("routines")]
    public List<DailyRoutine> Routines { get; set; } = new();
}
