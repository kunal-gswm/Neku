using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopPet.Services.Agent;

/// <summary>
/// Service managing real-time AI coding agent activity sessions,
/// event normalization, safe validation, deduplication, retention,
/// and local-first persistence.
/// </summary>
public sealed class AgentActivityService
{
    private readonly string _filePath;
    private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public const int MaxPayloadBytes = 65536; // 64 KB max payload
    public int MaxSessionsRetained { get; set; } = 50;
    public int MaxEventsPerSession { get; set; } = 100;

    // Deduplication tracking: (SessionId + EventType + ToolName) -> last received UTC ticks
    private readonly Dictionary<string, long> _recentEventSignatures = new();

    public event Action<AgentEvent>? EventReceived;
    public event Action<AgentSession>? SessionUpdated;
    public event Action? HistoryChanged;
    public event Action? AllSessionsIdle;

    public AgentActivityService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "agent_sessions.json");
        }

        Load();
    }

    /// <summary>
    /// Gets all tracked sessions in chronological order of last activity.
    /// </summary>
    public IReadOnlyList<AgentSession> GetSessions(string? filter = "all")
    {
        lock (_lock)
        {
            IEnumerable<AgentSession> query = _sessions.Values;

            query = (filter?.ToLowerInvariant()) switch
            {
                "active" => query.Where(s => s.Status == AgentSessionStatus.Active),
                "waiting" => query.Where(s => s.Status == AgentSessionStatus.WaitingForInput),
                "completed" => query.Where(s => s.Status == AgentSessionStatus.Completed),
                "failed" => query.Where(s => s.Status == AgentSessionStatus.Failed),
                _ => query
            };

            return query.OrderByDescending(s => s.LastActivityAt).ToList();
        }
    }

    /// <summary>
    /// Gets a specific session by its session ID.
    /// </summary>
    public AgentSession? GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.Trim(), out var s) ? s : null;
        }
    }

    /// <summary>
    /// Ingests and normalizes raw JSON string received over HTTP or IPC.
    /// Safely handles Claude Code hook envelopes, generic agent payloads, or legacy bodies.
    /// </summary>
    public (bool Success, string Message, AgentEvent? Event) IngestRawJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return (false, "Empty payload", null);

        if (rawJson.Length > MaxPayloadBytes)
            return (false, "Payload size exceeds 64KB limit", null);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            AgentEvent? evt = null;

            // 1. Detect Claude Code Hook Payload (has "hook_event_name")
            if (root.TryGetProperty("hook_event_name", out var hookProp))
            {
                evt = NormalizeClaudeCodePayload(root);
            }
            // 2. Detect Generic Agent Payload (has "event_type" or "provider_id")
            else if (root.TryGetProperty("event_type", out _) || root.TryGetProperty("provider_id", out _))
            {
                evt = NormalizeGenericPayload(root);
            }
            // 3. Fallback: try deserializing as generic payload
            else
            {
                evt = NormalizeGenericPayload(root);
            }

            if (evt == null || string.IsNullOrWhiteSpace(evt.SessionId))
                return (false, "Missing or invalid session identifier", null);

            bool recorded = RecordEvent(evt);
            return (recorded, recorded ? "Event accepted" : "Duplicate event dropped", evt);
        }
        catch (JsonException)
        {
            return (false, "Malformed JSON payload", null);
        }
        catch (Exception)
        {
            return (false, "Error processing agent event", null);
        }
    }

    /// <summary>
    /// Normalizes a Claude Code hook payload into an AgentEvent.
    /// </summary>
    private static AgentEvent NormalizeClaudeCodePayload(JsonElement root)
    {
        string sessionId = GetString(root, "session_id") ?? "claude-session";
        string cwd = GetString(root, "cwd") ?? "";
        string hookEvent = GetString(root, "hook_event_name") ?? "";
        string toolName = GetString(root, "tool_name") ?? "";
        string? error = GetString(root, "error");
        string? message = GetString(root, "message");
        string? notifType = GetString(root, "notification_type");
        string projectName = ExtractProjectName(cwd);

        string? filePath = null;
        string? commandText = null;

        if (root.TryGetProperty("tool_input", out var inputEl) && inputEl.ValueKind == JsonValueKind.Object)
        {
            filePath = GetString(inputEl, "file_path") ?? GetString(inputEl, "path");
            commandText = GetString(inputEl, "command");
        }

        var (eventType, status, summary, reqApproval) = MapClaudeHookEvent(
            hookEvent, toolName, filePath, commandText, projectName, error, message, notifType);

        string redactedCwd = AgentEvent.RedactPath(cwd);
        string? redactedFilePath = !string.IsNullOrWhiteSpace(filePath) ? AgentEvent.RedactPath(filePath) : null;
        string safeSummary = AgentEvent.SanitizeSecrets(SanitizeString(summary, 200));

        return new AgentEvent
        {
            ProviderId = "claude-code",
            ProviderName = "Claude Code",
            SessionId = SanitizeString(sessionId, 128),
            ProjectName = SanitizeString(projectName, 128),
            WorkingDirectory = SanitizeString(redactedCwd, 512),
            EventType = eventType,
            Timestamp = DateTime.Now,
            Summary = safeSummary,
            ToolName = !string.IsNullOrWhiteSpace(toolName) ? SanitizeString(toolName, 64) : null,
            FilePath = redactedFilePath != null ? SanitizeString(redactedFilePath, 512) : null,
            Status = status,
            RequiresApproval = reqApproval
        };
    }

    private static (AgentEventType Type, AgentEventStatus Status, string Summary, bool ReqApproval) MapClaudeHookEvent(
        string hookEvent, string toolName, string? filePath, string? commandText, string projectName,
        string? error, string? message, string? notifType)
    {
        string fileName = !string.IsNullOrWhiteSpace(filePath) ? Path.GetFileName(filePath) : "";

        return hookEvent.ToLowerInvariant() switch
        {
            "sessionstart" => (
                AgentEventType.SessionStarted,
                AgentEventStatus.Normal,
                $"Session started in {projectName}",
                false
            ),

            "pretooluse" => (
                AgentEventType.ToolWorking,
                AgentEventStatus.Normal,
                !string.IsNullOrEmpty(fileName)
                    ? $"Editing {fileName}"
                    : !string.IsNullOrEmpty(commandText)
                        ? $"Running: {AgentEvent.ExtractSafeCommand(commandText)}"
                        : !string.IsNullOrEmpty(toolName)
                            ? $"Tool call: {toolName}"
                            : "Agent is working...",
                false
            ),

            "posttooluse" => (
                string.Equals(toolName, "Edit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolName, "Write", StringComparison.OrdinalIgnoreCase)
                    ? AgentEventType.FileChanged
                    : AgentEventType.ToolWorking,
                AgentEventStatus.Normal,
                !string.IsNullOrEmpty(fileName)
                    ? $"Modified {fileName}"
                    : !string.IsNullOrEmpty(toolName)
                        ? $"Finished {toolName}"
                        : "Tool completed",
                false
            ),

            "posttoolusefailure" => (
                AgentEventType.TaskFailed,
                AgentEventStatus.Failure,
                !string.IsNullOrWhiteSpace(error)
                    ? $"Tool failed ({toolName}): {AgentEvent.SanitizeSecrets(Truncate(error, 50))}"
                    : !string.IsNullOrEmpty(toolName)
                        ? $"Tool failed: {toolName}"
                        : "Tool execution failed",
                false
            ),

            "permissionrequest" => (
                AgentEventType.WaitingForInput,
                AgentEventStatus.Waiting,
                !string.IsNullOrEmpty(toolName)
                    ? $"Waiting for permission: {toolName}"
                    : "Waiting for permission in terminal",
                true
            ),

            "notification" => (
                AgentEventType.WaitingForInput,
                AgentEventStatus.Waiting,
                !string.IsNullOrWhiteSpace(message)
                    ? AgentEvent.SanitizeSecrets(Truncate(message, 80))
                    : !string.IsNullOrWhiteSpace(notifType)
                        ? $"Notification: {notifType}"
                        : "Waiting for confirmation in terminal",
                true
            ),

            "stop" => (
                AgentEventType.TaskCompleted,
                AgentEventStatus.Success,
                "Agent completed turn",
                false
            ),

            "sessionend" => (
                AgentEventType.SessionEnded,
                AgentEventStatus.Normal,
                "Session finished",
                false
            ),

            _ => (
                AgentEventType.Unknown,
                AgentEventStatus.Normal,
                !string.IsNullOrWhiteSpace(hookEvent) ? $"Event: {hookEvent}" : "Agent activity",
                false
            )
        };
    }

    /// <summary>
    /// Normalizes a generic JSON payload.
    /// </summary>
    private static AgentEvent NormalizeGenericPayload(JsonElement root)
    {
        string sessionId = GetString(root, "session_id") ?? GetString(root, "sessionId") ?? "session-" + Guid.NewGuid().ToString("N")[..8];
        string providerId = GetString(root, "provider_id") ?? GetString(root, "providerId") ?? "agent";
        string providerName = GetString(root, "provider_name") ?? GetString(root, "providerName") ?? "Coding Agent";
        string cwd = GetString(root, "working_directory") ?? GetString(root, "cwd") ?? "";
        string projectName = GetString(root, "project_name") ?? GetString(root, "projectName") ?? ExtractProjectName(cwd);
        string eventTypeStr = GetString(root, "event_type") ?? GetString(root, "eventType") ?? "tool_working";
        string summary = GetString(root, "summary") ?? GetString(root, "message") ?? "Agent activity";
        string? toolName = GetString(root, "tool_name") ?? GetString(root, "toolName");
        string? filePath = GetString(root, "file_path") ?? GetString(root, "filePath");
        string statusStr = GetString(root, "status") ?? "normal";
        bool reqApproval = root.TryGetProperty("requires_approval", out var rProp) && rProp.GetBoolean();

        AgentEventType type = ParseEventType(eventTypeStr);
        AgentEventStatus status = ParseEventStatus(statusStr);

        string redactedCwd = AgentEvent.RedactPath(cwd);
        string? redactedFilePath = !string.IsNullOrWhiteSpace(filePath) ? AgentEvent.RedactPath(filePath) : null;
        string safeSummary = AgentEvent.SanitizeSecrets(SanitizeString(summary, 200));

        return new AgentEvent
        {
            ProviderId = SanitizeString(providerId, 64),
            ProviderName = SanitizeString(providerName, 64),
            SessionId = SanitizeString(sessionId, 128),
            ProjectName = SanitizeString(projectName, 128),
            WorkingDirectory = SanitizeString(redactedCwd, 512),
            EventType = type,
            Timestamp = DateTime.Now,
            Summary = safeSummary,
            ToolName = !string.IsNullOrWhiteSpace(toolName) ? SanitizeString(toolName, 64) : null,
            FilePath = redactedFilePath != null ? SanitizeString(redactedFilePath, 512) : null,
            Status = status,
            RequiresApproval = reqApproval
        };
    }

    /// <summary>
    /// Records an event into the session tracking hierarchy.
    /// Returns false if dropped as a duplicate.
    /// </summary>
    public bool RecordEvent(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (string.IsNullOrWhiteSpace(evt.SessionId)) return false;

        lock (_lock)
        {
            // 1. Deduplication check (within 300ms)
            string sig = $"{evt.SessionId}|{evt.EventType}|{evt.ToolName ?? ""}";
            long nowTicks = DateTime.UtcNow.Ticks;
            if (_recentEventSignatures.TryGetValue(sig, out long lastTicks))
            {
                if (TimeSpan.FromTicks(nowTicks - lastTicks).TotalMilliseconds < 300)
                {
                    return false; // Duplicate
                }
            }
            _recentEventSignatures[sig] = nowTicks;

            // 2. Fetch or create session
            if (!_sessions.TryGetValue(evt.SessionId, out var session))
            {
                session = new AgentSession
                {
                    SessionId = evt.SessionId,
                    ProviderId = evt.ProviderId,
                    ProviderName = evt.ProviderName,
                    ProjectName = evt.ProjectName,
                    WorkingDirectory = evt.WorkingDirectory,
                    StartedAt = evt.Timestamp,
                    LastActivityAt = evt.Timestamp,
                    Status = AgentSessionStatus.Active
                };
                _sessions[evt.SessionId] = session;
            }

            // 3. Update session metadata
            session.LastActivityAt = evt.Timestamp > session.LastActivityAt ? evt.Timestamp : session.LastActivityAt;
            session.LastSummary = evt.Summary;

            if (!string.IsNullOrWhiteSpace(evt.ToolName))
            {
                session.CurrentTool = evt.ToolName;
                session.TotalToolCalls++;
            }

            if (!string.IsNullOrWhiteSpace(evt.FilePath))
            {
                session.CurrentFile = evt.FilePath;
                if (evt.EventType == AgentEventType.FileChanged)
                    session.TotalFilesChanged++;
            }

            // 4. Update session status based on event type
            switch (evt.EventType)
            {
                case AgentEventType.SessionStarted:
                case AgentEventType.PromptSubmitted:
                case AgentEventType.ToolWorking:
                case AgentEventType.FileChanged:
                    session.Status = AgentSessionStatus.Active;
                    break;

                case AgentEventType.WaitingForInput:
                    session.Status = AgentSessionStatus.WaitingForInput;
                    break;

                case AgentEventType.TaskCompleted:
                    session.Status = AgentSessionStatus.Completed;
                    break;

                case AgentEventType.TaskFailed:
                    session.Status = AgentSessionStatus.Failed;
                    break;

                case AgentEventType.SessionEnded:
                    if (session.Status == AgentSessionStatus.Active)
                        session.Status = AgentSessionStatus.Completed;
                    break;
            }

            // 5. Insert event in chronological order
            int insertIndex = session.Events.FindIndex(e => e.Timestamp > evt.Timestamp);
            if (insertIndex >= 0)
                session.Events.Insert(insertIndex, evt);
            else
                session.Events.Add(evt);

            // 6. Enforce event retention per session
            while (session.Events.Count > MaxEventsPerSession)
            {
                session.Events.RemoveAt(0);
            }

            // 7. Enforce max sessions retained
            PruneSessions();

            Save();

            EventReceived?.Invoke(evt);
            SessionUpdated?.Invoke(session);
            HistoryChanged?.Invoke();

            return true;
        }
    }

    /// <summary>
    /// Checks for active sessions that have become idle (> 5 minutes without activity).
    /// Fires AllSessionsIdle if all tracked sessions are completed/failed/idle.
    /// </summary>
    public void CheckIdleSessions(TimeSpan? idleTimeout = null)
    {
        var timeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        bool anyChanged = false;
        bool hasActiveSessions = false;

        lock (_lock)
        {
            var now = DateTime.Now;
            foreach (var session in _sessions.Values)
            {
                if (session.Status == AgentSessionStatus.Active)
                {
                    if (now - session.LastActivityAt > timeout)
                    {
                        session.Status = AgentSessionStatus.Idle;
                        anyChanged = true;
                    }
                    else
                    {
                        hasActiveSessions = true;
                    }
                }
                else if (session.Status == AgentSessionStatus.WaitingForInput)
                {
                    hasActiveSessions = true;
                }
            }

            if (anyChanged)
            {
                Save();
                HistoryChanged?.Invoke();
            }

            if (!hasActiveSessions && _sessions.Count > 0)
            {
                AllSessionsIdle?.Invoke();
            }
        }
    }

    /// <summary>
    /// Clears completed, failed, and idle session history without deleting active sessions.
    /// </summary>
    public int ClearCompletedHistory()
    {
        lock (_lock)
        {
            var keysToRemove = _sessions.Values
                .Where(s => s.Status is AgentSessionStatus.Completed or AgentSessionStatus.Failed or AgentSessionStatus.Idle)
                .Select(s => s.SessionId)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _sessions.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                Save();
                HistoryChanged?.Invoke();
            }

            return keysToRemove.Count;
        }
    }

    /// <summary>
    /// Clears all agent session history.
    /// </summary>
    public void ClearAllHistory()
    {
        lock (_lock)
        {
            _sessions.Clear();
            Save();
            HistoryChanged?.Invoke();
        }
    }

    private void PruneSessions()
    {
        if (_sessions.Count <= MaxSessionsRetained) return;

        // Remove oldest non-active sessions first
        var sorted = _sessions.Values
            .OrderBy(s => s.Status == AgentSessionStatus.Active ? 1 : 0)
            .ThenBy(s => s.LastActivityAt)
            .ToList();

        while (_sessions.Count > MaxSessionsRetained && sorted.Count > 0)
        {
            var toRemove = sorted[0];
            sorted.RemoveAt(0);
            _sessions.Remove(toRemove.SessionId);
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return;

            try
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<AgentSession>>(json, JsonOpts);
                if (list != null)
                {
                    _sessions.Clear();
                    foreach (var s in list)
                    {
                        if (!string.IsNullOrWhiteSpace(s.SessionId))
                            _sessions[s.SessionId] = s;
                    }
                }
            }
            catch
            {
                // Corrupt file recovery: keep running with fresh sessions
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tempFile = $"{_filePath}.tmp";
                string json = JsonSerializer.Serialize(_sessions.Values.ToList(), JsonOpts);
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _filePath, overwrite: true);
            }
            catch
            {
                // Non-fatal persistence error
            }
        }
    }

    private static string? GetString(JsonElement el, string propName)
    {
        if (el.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString()?.Trim();
        return null;
    }

    private static string ExtractProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "Workspace";
        try
        {
            string trimmed = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return !string.IsNullOrWhiteSpace(name) ? name : "Workspace";
        }
        catch
        {
            return "Workspace";
        }
    }

    private static string SanitizeString(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string cleaned = new string(input.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').ToArray()).Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static AgentEventType ParseEventType(string? str) => str?.ToLowerInvariant() switch
    {
        "session_started" or "sessionstarted" or "start" => AgentEventType.SessionStarted,
        "prompt_submitted" or "promptsubmitted" or "prompt" => AgentEventType.PromptSubmitted,
        "tool_working" or "toolworking" or "tool" => AgentEventType.ToolWorking,
        "file_changed" or "filechanged" or "file" => AgentEventType.FileChanged,
        "waiting_for_input" or "waitingforinput" or "waiting" => AgentEventType.WaitingForInput,
        "task_completed" or "taskcompleted" or "completed" or "success" => AgentEventType.TaskCompleted,
        "task_failed" or "taskfailed" or "failed" or "error" => AgentEventType.TaskFailed,
        "session_ended" or "sessionended" or "end" => AgentEventType.SessionEnded,
        _ => AgentEventType.Unknown
    };

    private static AgentEventStatus ParseEventStatus(string? str) => str?.ToLowerInvariant() switch
    {
        "success" or "ok" or "pass" => AgentEventStatus.Success,
        "failure" or "fail" or "error" => AgentEventStatus.Failure,
        "waiting" or "prompt" => AgentEventStatus.Waiting,
        _ => AgentEventStatus.Normal
    };
}
