using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public enum FocusSessionType
{
    Focus = 0,
    ShortBreak = 1,
    LongBreak = 2
}

public enum FocusSessionStatus
{
    Completed = 0,
    Interrupted = 1,
    Cancelled = 2
}

public sealed class FocusSessionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("startUtc")]
    public DateTime StartUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("endUtc")]
    public DateTime EndUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sessionType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FocusSessionType SessionType { get; set; } = FocusSessionType.Focus;

    [JsonPropertyName("targetMinutes")]
    public int TargetMinutes { get; set; } = 25;

    [JsonPropertyName("actualSeconds")]
    public double ActualSeconds { get; set; } = 0;

    [JsonPropertyName("pausedSeconds")]
    public double PausedSeconds { get; set; } = 0;

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FocusSessionStatus Status { get; set; } = FocusSessionStatus.Completed;

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("taskTitle")]
    public string? TaskTitle { get; set; }

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonIgnore]
    public DateTime StartLocal => StartUtc.ToLocalTime();

    [JsonIgnore]
    public DateTime EndLocal => EndUtc.ToLocalTime();

    [JsonIgnore]
    public double ActualMinutes => Math.Round(ActualSeconds / 60.0, 1);

    [JsonIgnore]
    public string DurationDisplay => ActualSeconds >= 60
        ? $"{(int)(ActualSeconds / 60)}m {(int)(ActualSeconds % 60):D2}s"
        : $"{(int)ActualSeconds}s";

    [JsonIgnore]
    public string StatusDisplay => Status switch
    {
        FocusSessionStatus.Completed => "Completed ✅",
        FocusSessionStatus.Interrupted => "Interrupted ⚠️",
        FocusSessionStatus.Cancelled => "Cancelled ❌",
        _ => Status.ToString()
    };

    [JsonIgnore]
    public string TypeDisplay => SessionType switch
    {
        FocusSessionType.ShortBreak => "☕ Short Break",
        FocusSessionType.LongBreak => "🌴 Long Break",
        _ => "🍅 Focus"
    };

    [JsonIgnore]
    public string FormattedDate => StartLocal.ToString("MMM d, yyyy");

    [JsonIgnore]
    public string FormattedTime => $"{StartLocal:HH:mm} – {EndLocal:HH:mm}";
}

public sealed class FocusHistoryDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sessions")]
    public List<FocusSessionRecord> Sessions { get; set; } = new();
}
