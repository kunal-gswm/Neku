using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

/// <summary>
/// A single action step within a Daily Routine.
/// </summary>
public sealed class RoutineStep : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "";
    private int _estimatedMinutes = 0;
    private string _notes = "";
    private bool _isCompleted = false;
    private bool _isSkipped = false;
    private DateTime? _completedAt;

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

    [JsonPropertyName("estimatedMinutes")]
    public int EstimatedMinutes
    {
        get => _estimatedMinutes;
        set { if (_estimatedMinutes != value) { _estimatedMinutes = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("notes")]
    public string Notes
    {
        get => _notes;
        set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
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
                if (_isCompleted) _isSkipped = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ActionIcon));
            }
        }
    }

    [JsonPropertyName("isSkipped")]
    public bool IsSkipped
    {
        get => _isSkipped;
        set
        {
            if (_isSkipped != value)
            {
                _isSkipped = value;
                if (_isSkipped) _isCompleted = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ActionIcon));
            }
        }
    }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { if (_completedAt != value) { _completedAt = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public string StatusDisplay => IsCompleted ? "Done ✅" : (IsSkipped ? "Skipped ⏭️" : "Pending");

    [JsonIgnore]
    public string ActionIcon => IsCompleted ? "✅" : (IsSkipped ? "⏭️" : "⭕");
}

/// <summary>
/// A user-defined reusable group of tasks/steps intended to be executed sequentially or together.
/// Supports starting, progressing, skipping steps, resuming, and persistent local state.
/// </summary>
public sealed class DailyRoutine : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "New Routine";
    private string _description = "";
    private string _icon = "⚡";
    private List<RoutineStep> _steps = new();

    private bool _isActive = false;
    private DateTime? _startedAt;
    private DateTime? _lastCompletedAt;
    private int _currentStepIndex = 0;

    private DateTime _createdAt = DateTime.UtcNow;
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

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("icon")]
    public string Icon
    {
        get => _icon;
        set { if (_icon != value) { _icon = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("steps")]
    public List<RoutineStep> Steps
    {
        get => _steps;
        set
        {
            _steps = value ?? new List<RoutineStep>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressSummary));
            OnPropertyChanged(nameof(TotalEstimatedMinutes));
        }
    }

    [JsonPropertyName("isActive")]
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }
    }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt
    {
        get => _startedAt;
        set { if (_startedAt != value) { _startedAt = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("lastCompletedAt")]
    public DateTime? LastCompletedAt
    {
        get => _lastCompletedAt;
        set { if (_lastCompletedAt != value) { _lastCompletedAt = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("currentStepIndex")]
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set { if (_currentStepIndex != value) { _currentStepIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStep)); } }
    }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set { if (_updatedAt != value) { _updatedAt = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public int CompletedStepsCount => Steps.Count(s => s.IsCompleted);

    [JsonIgnore]
    public int TotalStepsCount => Steps.Count;

    [JsonIgnore]
    public bool IsFinished => Steps.Count > 0 && Steps.All(s => s.IsCompleted || s.IsSkipped);

    [JsonIgnore]
    public string ProgressSummary => Steps.Count == 0
        ? "0 steps"
        : $"{CompletedStepsCount}/{Steps.Count} steps completed";

    [JsonIgnore]
    public int TotalEstimatedMinutes => Steps.Sum(s => s.EstimatedMinutes);

    [JsonIgnore]
    public RoutineStep? CurrentStep
    {
        get
        {
            if (Steps.Count == 0) return null;
            if (CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count)
            {
                return Steps[CurrentStepIndex];
            }
            return Steps.FirstOrDefault(s => !s.IsCompleted && !s.IsSkipped);
        }
    }

    [JsonIgnore]
    public string StatusDisplay
    {
        get
        {
            if (IsActive)
            {
                return IsFinished ? "🎉 Completed!" : $"In Progress ({ProgressSummary})";
            }
            return LastCompletedAt.HasValue
                ? $"Last run {LastCompletedAt.Value.ToLocalTime():MMM d}"
                : "Ready to start";
        }
    }
}
