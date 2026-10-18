using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DesktopPet.Services.Agent;

/// <summary>
/// Normalized event types emitted across AI coding agent providers.
/// </summary>
public enum AgentEventType
{
    SessionStarted,
    PromptSubmitted,
    ToolWorking,
    FileChanged,
    WaitingForInput,
    TaskCompleted,
    TaskFailed,
    SessionEnded,
    Unknown
}

/// <summary>
/// Event status state.
/// </summary>
public enum AgentEventStatus
{
    Normal,
    Success,
    Failure,
    Waiting
}

/// <summary>
/// Lifecycle state of a tracked coding agent session.
/// </summary>
public enum AgentSessionStatus
{
    Active,
    WaitingForInput,
    Completed,
    Failed,
    Idle
}

/// <summary>
/// Provider-neutral normalized agent event model.
/// </summary>
public sealed class AgentEvent
{
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "unknown";

    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = "Coding Agent";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "Workspace";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("eventType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentEventType EventType { get; set; } = AgentEventType.Unknown;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentEventStatus Status { get; set; } = AgentEventStatus.Normal;

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; }

    // UI Helper properties
    [JsonIgnore]
    public string DisplayTime => Timestamp.ToString("HH:mm:ss");

    [JsonIgnore]
    public string DisplayIcon => EventType switch
    {
        AgentEventType.SessionStarted => "🚀",
        AgentEventType.PromptSubmitted => "💬",
        AgentEventType.ToolWorking => "⚙️",
        AgentEventType.FileChanged => "📝",
        AgentEventType.WaitingForInput => "⏳",
        AgentEventType.TaskCompleted => "✅",
        AgentEventType.TaskFailed => "❌",
        AgentEventType.SessionEnded => "⏹️",
        _ => "ℹ️"
    };

    [JsonIgnore]
    public string DisplayTypeName => EventType switch
    {
        AgentEventType.SessionStarted => "Session Started",
        AgentEventType.PromptSubmitted => "Prompt Submitted",
        AgentEventType.ToolWorking => "Tool Working",
        AgentEventType.FileChanged => "File Changed",
        AgentEventType.WaitingForInput => "Waiting for Input",
        AgentEventType.TaskCompleted => "Task Completed",
        AgentEventType.TaskFailed => "Task Failed",
        AgentEventType.SessionEnded => "Session Ended",
        _ => "Event"
    };

    [JsonIgnore]
    public string StatusBadgeColor => Status switch
    {
        AgentEventStatus.Success => "#22C55E",
        AgentEventStatus.Failure => "#EF4444",
        AgentEventStatus.Waiting => "#F59E0B",
        _ => "#8B5CF6"
    };

    [JsonIgnore]
    public string RedactedFilePath => RedactPath(FilePath);

    public static string RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[userProfile.Length..];
        }
        return path;
    }

    private static readonly System.Text.RegularExpressions.Regex SecretPattern = new(
        @"(?i)(sk-[a-zA-Z0-9_\-]{20,}|gh[pousr]_[a-zA-Z0-9]{20,}|github_pat_[a-zA-Z0-9_\-]{20,}|AKIA[0-9A-Z]{16}|bearer\s+[a-zA-Z0-9_\-\.]{15,}|(?<=password|passwd|secret|api_key|apikey|access_token|auth_token|token)\s*[:=]\s*[^\s,;]+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Redacts known secret patterns (API keys, tokens, passwords) to protect user privacy.
    /// </summary>
    public static string SanitizeSecrets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return SecretPattern.Replace(text, "=[REDACTED]");
    }

    /// <summary>
    /// Extracts a safe, concise command summary from arbitrary command text without full arguments or secrets.
    /// </summary>
    public static string ExtractSafeCommand(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return "Command";

        // Take only first line and split on pipes, redirects, or chaining operators
        string firstLine = commandText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)[0].Trim();
        int opIdx = firstLine.IndexOfAny(['|', ';', '&', '>']);
        if (opIdx > 0)
        {
            firstLine = firstLine[..opIdx].Trim();
        }

        // Tokenize by space to extract executable and subcommands (e.g., "git status", "dotnet test", "npm run build")
        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "Command";

        string exe = Path.GetFileName(tokens[0]);
        string result;
        if (tokens.Length > 1 && !tokens[1].StartsWith('-'))
        {
            result = $"{exe} {tokens[1]}";
            if (tokens.Length > 2 && !tokens[2].StartsWith('-'))
                result += $" {tokens[2]}";
        }
        else
        {
            result = exe;
        }

        if (result.Length > 50) result = result[..50] + "…";
        return SanitizeSecrets(result);
    }
}

/// <summary>
/// Represents a tracked coding session containing chronological events.
/// </summary>
public sealed class AgentSession
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "unknown";

    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = "Coding Agent";

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "Workspace";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentSessionStatus Status { get; set; } = AgentSessionStatus.Active;

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("lastActivityAt")]
    public DateTime LastActivityAt { get; set; } = DateTime.Now;

    [JsonPropertyName("lastSummary")]
    public string LastSummary { get; set; } = "";

    [JsonPropertyName("currentTool")]
    public string? CurrentTool { get; set; }

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("totalToolCalls")]
    public int TotalToolCalls { get; set; }

    [JsonPropertyName("totalFilesChanged")]
    public int TotalFilesChanged { get; set; }

    [JsonPropertyName("events")]
    public List<AgentEvent> Events { get; set; } = new();

    // UI Helper properties
    [JsonIgnore]
    public string ShortSessionId => SessionId.Length > 12 ? SessionId[..12] + "…" : SessionId;

    [JsonIgnore]
    public string RedactedWorkingDirectory => AgentEvent.RedactPath(WorkingDirectory);

    [JsonIgnore]
    public string StatusDisplayText => Status switch
    {
        AgentSessionStatus.Active => "Working",
        AgentSessionStatus.WaitingForInput => "Waiting for Input",
        AgentSessionStatus.Completed => "Completed",
        AgentSessionStatus.Failed => "Failed",
        AgentSessionStatus.Idle => "Idle",
        _ => "Unknown"
    };

    [JsonIgnore]
    public string StatusBadgeColor => Status switch
    {
        AgentSessionStatus.Active => "#3B82F6",
        AgentSessionStatus.WaitingForInput => "#F59E0B",
        AgentSessionStatus.Completed => "#22C55E",
        AgentSessionStatus.Failed => "#EF4444",
        AgentSessionStatus.Idle => "#94A3B8",
        _ => "#8B5CF6"
    };

    [JsonIgnore]
    public string RelativeLastActivity
    {
        get
        {
            var diff = DateTime.Now - LastActivityAt;
            if (diff.TotalSeconds < 10) return "Just now";
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return $"{(int)diff.TotalDays}d ago";
        }
    }

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            var duration = (Status is AgentSessionStatus.Completed or AgentSessionStatus.Failed ? LastActivityAt : DateTime.Now) - StartedAt;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            if (duration.TotalMinutes < 1) return $"{(int)duration.TotalSeconds}s";
            if (duration.TotalHours < 1) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }
    }

    /// <summary>
    /// Safely resolves a path containing ~ back to the local user profile directory for file opening.
    /// </summary>
    public static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.StartsWith('~'))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string remainder = path.Length > 1 && (path[1] == '/' || path[1] == '\\') ? path[2..] : path[1..];
            return Path.Combine(userProfile, remainder);
        }
        return path;
    }
}

/// <summary>
/// Raw Claude Code hook JSON envelope schema received on stdin.
/// </summary>
public sealed class ClaudeCodeHookPayload
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("tool_input")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; set; }

    [JsonPropertyName("stop_hook_active")]
    public bool? StopHookActive { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Generic provider payload for custom or third-party agent tools.
/// </summary>
public sealed class GenericAgentEventDto
{
    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("project_name")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("requires_approval")]
    public bool? RequiresApproval { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}
