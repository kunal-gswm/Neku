using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

/// <summary>
/// Authoritative local specification for a persistent recurring task schedule.
/// Decoupled from individual task occurrences to allow independent completion,
/// skipping, pausing, and history preservation.
/// </summary>
public sealed class RecurringSchedule : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "New Recurring Task";
    private string _description = "";
    private TaskPriority _priority = TaskPriority.Normal;
    private int _estimatedMinutes = 0;
    private string _project = "";
    private List<string> _tags = new();
    private List<string> _subtaskTemplates = new();

    private TaskRecurrenceRule _recurrenceRule = TaskRecurrenceRule.Daily;
    private List<DayOfWeek> _daysOfWeek = new();
    private int _dayOfMonth = 1;
    private string _scheduledTime = "09:00";
    private string _timeZoneId = TimeZoneInfo.Local.Id;
    private int? _reminderMinutesBefore = 0;

    private bool _isPaused = false;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private DateTime? _lastOccurrenceDate;

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

    [JsonPropertyName("priority")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskPriority Priority
    {
        get => _priority;
        set { if (_priority != value) { _priority = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("estimatedMinutes")]
    public int EstimatedMinutes
    {
        get => _estimatedMinutes;
        set { if (_estimatedMinutes != value) { _estimatedMinutes = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("project")]
    public string Project
    {
        get => _project;
        set { if (_project != value) { _project = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set { _tags = value ?? new List<string>(); OnPropertyChanged(); }
    }

    [JsonPropertyName("subtaskTemplates")]
    public List<string> SubtaskTemplates
    {
        get => _subtaskTemplates;
        set { _subtaskTemplates = value ?? new List<string>(); OnPropertyChanged(); }
    }

    [JsonPropertyName("recurrenceRule")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskRecurrenceRule RecurrenceRule
    {
        get => _recurrenceRule;
        set { if (_recurrenceRule != value) { _recurrenceRule = value; OnPropertyChanged(); OnPropertyChanged(nameof(RuleDisplay)); } }
    }

    /// <summary>
    /// For Weekly recurrence: specific days of week to repeat (e.g. Monday, Thursday).
    /// If empty and rule is Weekly, defaults to StartDate's DayOfWeek.
    /// </summary>
    [JsonPropertyName("daysOfWeek")]
    public List<DayOfWeek> DaysOfWeek
    {
        get => _daysOfWeek;
        set { _daysOfWeek = value ?? new List<DayOfWeek>(); OnPropertyChanged(); OnPropertyChanged(nameof(RuleDisplay)); }
    }

    /// <summary>
    /// For Monthly recurrence: target day of month (1-31).
    /// If month has fewer days (e.g. Feb 28/29, Apr 30), clamps deterministically to month's last valid day.
    /// </summary>
    [JsonPropertyName("dayOfMonth")]
    public int DayOfMonth
    {
        get => _dayOfMonth;
        set { if (_dayOfMonth != value) { _dayOfMonth = Math.Clamp(value, 1, 31); OnPropertyChanged(); OnPropertyChanged(nameof(RuleDisplay)); } }
    }

    /// <summary>
    /// Local time of day in 24h format (e.g. "19:00" for 7 PM).
    /// </summary>
    [JsonPropertyName("scheduledTime")]
    public string ScheduledTime
    {
        get => _scheduledTime;
        set { if (_scheduledTime != value) { _scheduledTime = value ?? "09:00"; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Preserved Windows / IANA TimeZone identifier to anchor occurrences across clock shifts.
    /// </summary>
    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId
    {
        get => _timeZoneId;
        set { if (_timeZoneId != value) { _timeZoneId = value ?? TimeZoneInfo.Local.Id; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Offset in minutes before due time to fire reminder (0 = at due time). Null = no reminder.
    /// </summary>
    [JsonPropertyName("reminderMinutesBefore")]
    public int? ReminderMinutesBefore
    {
        get => _reminderMinutesBefore;
        set { if (_reminderMinutesBefore != value) { _reminderMinutesBefore = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("isPaused")]
    public bool IsPaused
    {
        get => _isPaused;
        set { if (_isPaused != value) { _isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }
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

    [JsonPropertyName("startDate")]
    public DateTime? StartDate
    {
        get => _startDate;
        set { if (_startDate != value) { _startDate = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate
    {
        get => _endDate;
        set { if (_endDate != value) { _endDate = value; OnPropertyChanged(); } }
    }

    [JsonPropertyName("lastOccurrenceDate")]
    public DateTime? LastOccurrenceDate
    {
        get => _lastOccurrenceDate;
        set { if (_lastOccurrenceDate != value) { _lastOccurrenceDate = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public string StatusDisplay => IsPaused ? "⏸️ Paused" : "Active";

    [JsonIgnore]
    public string RuleDisplay
    {
        get
        {
            return RecurrenceRule switch
            {
                TaskRecurrenceRule.Daily => "🔁 Every day",
                TaskRecurrenceRule.Weekdays => "🔁 Every weekday (Mon–Fri)",
                TaskRecurrenceRule.Weekly => DaysOfWeek.Count > 0
                    ? $"🔁 Weekly on {string.Join(", ", DaysOfWeek.Select(d => d.ToString()[..3]))}"
                    : "🔁 Weekly",
                TaskRecurrenceRule.Monthly => $"🔁 Monthly on day {DayOfMonth}",
                _ => "None"
            };
        }
    }
}
