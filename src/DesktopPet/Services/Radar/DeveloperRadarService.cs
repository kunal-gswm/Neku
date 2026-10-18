using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DesktopPet.Services.Agent;
using DesktopPet.Services.DeskSense;

namespace DesktopPet.Services.Radar;

/// <summary>
/// Unified Developer Radar coordinator integrating desktop context,
/// monitored Git repositories, build/test records, and coding-agent activity.
/// </summary>
public sealed class DeveloperRadarService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DeskSenseService _deskSense;
    private readonly RepositoryMonitorService _repoMonitor;
    private readonly DeveloperEventService? _developerEvents;
    private readonly AgentActivityService? _agentActivity;
    private readonly FocusTimerService? _focusTimer;

    private readonly List<BuildTestStatusRecord> _buildRecords = new();
    private readonly object _lock = new();

    public DeskSenseContext CurrentContext => _deskSense.CurrentContext;
    public RepositoryMonitorService RepoMonitor => _repoMonitor;
    public string? SelectedRepositoryPath
    {
        get => _settings.SelectedRepositoryPath;
        set => _settings.SelectedRepositoryPath = value;
    }

    public event Action? RadarStateChanged;

    public DeveloperRadarService(
        AppSettings settings,
        DeskSenseService deskSense,
        RepositoryMonitorService repoMonitor,
        DeveloperEventService? developerEvents = null,
        AgentActivityService? agentActivity = null,
        FocusTimerService? focusTimer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _deskSense = deskSense ?? throw new ArgumentNullException(nameof(deskSense));
        _repoMonitor = repoMonitor ?? throw new ArgumentNullException(nameof(repoMonitor));
        _developerEvents = developerEvents;
        _agentActivity = agentActivity;
        _focusTimer = focusTimer;

        _deskSense.ContextUpdated += _ => OnRadarUpdated();
        _deskSense.TransitionOccurred += (_, _) => OnRadarUpdated();
        _repoMonitor.RepositoryUpdated += _ => OnRadarUpdated();
        _repoMonitor.RepositoriesChanged += OnRadarUpdated;

        if (_agentActivity != null)
        {
            _agentActivity.EventReceived += OnAgentEventReceived;
            _agentActivity.HistoryChanged += OnRadarUpdated;
        }

        if (_developerEvents != null)
        {
            _developerEvents.BuildTestEventReceived += OnDeveloperBuildTestReceived;
        }

        if (_focusTimer != null)
        {
            _focusTimer.StateChanged += OnTimerStateChanged;
        }
    }

    public MonitoredRepository? GetSelectedRepository()
    {
        var repos = _repoMonitor.GetRepositories();
        if (repos.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(SelectedRepositoryPath))
        {
            var match = repos.FirstOrDefault(r => string.Equals(r.Path, SelectedRepositoryPath, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return repos[0];
    }

    public void RecordBuildTestResult(string projectName, BuildTestResultStatus status, string source, string message)
    {
        var record = new BuildTestStatusRecord
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Project" : projectName,
            Status = status,
            Source = source,
            Message = message,
            Confidence = "High",
            Timestamp = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _buildRecords.Insert(0, record);
            if (_buildRecords.Count > 50) _buildRecords.RemoveAt(_buildRecords.Count - 1);
        }

        OnRadarUpdated();
    }

    public BuildTestStatusRecord? GetLatestBuildRecord()
    {
        lock (_lock)
        {
            return _buildRecords.Count > 0 ? _buildRecords[0] : null;
        }
    }

    public IReadOnlyList<BuildTestStatusRecord> GetBuildRecords()
    {
        lock (_lock)
        {
            return _buildRecords.ToList();
        }
    }

    public int GetActiveAgentSessionCount()
    {
        return _agentActivity?.GetSessions("active").Count ?? 0;
    }

    public string GetFocusTimerSummary()
    {
        if (_focusTimer == null || _focusTimer.State == FocusTimerState.Idle)
            return "Idle";

        string stateName = _focusTimer.State switch
        {
            FocusTimerState.Focusing => "Focusing",
            FocusTimerState.FocusPaused => "Paused",
            FocusTimerState.Break => "Break",
            FocusTimerState.BreakPaused => "Break Paused",
            _ => "Idle"
        };

        return $"{stateName} ({_focusTimer.FormattedRemaining})";
    }

    // ── Interaction Helpers ────────────────────────────────────────────────

    public bool OpenInExplorer(string? path = null)
    {
        string? targetPath = path ?? GetSelectedRepository()?.Path;
        if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", targetPath) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool CopyRepositoryPath(string? path = null)
    {
        string? targetPath = path ?? GetSelectedRepository()?.Path;
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        try
        {
            System.Windows.Clipboard.SetText(targetPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool CopyPathToClipboard(string? path = null) => CopyRepositoryPath(path);

    private void OnDeveloperBuildTestReceived(string project, BuildTestResultStatus status, string message)
    {
        RecordBuildTestResult(project, status, "Webhook", message);
    }

    private void OnTimerStateChanged(FocusTimerState state) => OnRadarUpdated();

    private void OnAgentEventReceived(AgentEvent evt)
    {
        if (evt.EventType == AgentEventType.TaskCompleted)
        {
            RecordBuildTestResult(evt.ProjectName, BuildTestResultStatus.Success, "Coding Agent", evt.Summary);
        }
        else if (evt.EventType == AgentEventType.TaskFailed)
        {
            RecordBuildTestResult(evt.ProjectName, BuildTestResultStatus.Failure, "Coding Agent", evt.Summary);
        }
    }

    private void OnRadarUpdated()
    {
        RadarStateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_agentActivity != null)
        {
            _agentActivity.EventReceived -= OnAgentEventReceived;
            _agentActivity.HistoryChanged -= OnRadarUpdated;
        }

        if (_developerEvents != null)
        {
            _developerEvents.BuildTestEventReceived -= OnDeveloperBuildTestReceived;
        }

        if (_focusTimer != null)
        {
            _focusTimer.StateChanged -= OnTimerStateChanged;
        }
    }
}
