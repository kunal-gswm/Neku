using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public sealed class TaskItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "New Task";
    private string _description = "";
    private bool _isCompleted = false;
    private TaskPriority _priority = TaskPriority.Normal;
    private DateTime? _dueDate;
    private int _estimatedMinutes = 0;
    private int _completedPomodoros = 0;
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

    // Helper properties for UI data binding
    [JsonIgnore]
    public bool IsOverdue => !IsCompleted && DueDate.HasValue && ToLocalDate(DueDate.Value) < DateTime.Today;

    [JsonIgnore]
    public bool IsDueToday => !IsCompleted && DueDate.HasValue && ToLocalDate(DueDate.Value) == DateTime.Today;

    [JsonIgnore]
    public string FormattedDueDate => DueDate.HasValue ? ToLocalDate(DueDate.Value).ToString("MMM d") : "";

    [JsonIgnore]
    public string PriorityDisplay => Priority switch
    {
        TaskPriority.High => "High",
        TaskPriority.Low => "Low",
        _ => "Normal"
    };

    [JsonIgnore]
    public string PomodoroDisplay => CompletedPomodoros > 0 ? $"🍅 {CompletedPomodoros}" : "";

    [JsonIgnore]
    public string StatusDisplay => IsCompleted ? "Completed" : (IsOverdue ? "Overdue" : (IsDueToday ? "Due Today" : "Pending"));
}

public sealed class TaskDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("tasks")]
    public List<TaskItem> Tasks { get; set; } = new();
}
