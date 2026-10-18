using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public enum TimeRangePreset
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    ThisWeek,
    ThisMonth,
    AllTime,
    Custom
}

public sealed class DayProductivityMetric
{
    public DateTime Date { get; set; }
    public string DayLabel { get; set; } = "";
    public double FocusMinutes { get; set; }
    public int CompletedSessions { get; set; }
    public int CompletedTasks { get; set; }
}

public sealed class ProjectFocusMetric
{
    public string ProjectName { get; set; } = "Uncategorized";
    public double FocusMinutes { get; set; }
    public double Percentage { get; set; }
    public int SessionCount { get; set; }
}

public sealed class ProductivitySummary
{
    public double TotalFocusMinutes { get; set; }
    public double TotalFocusHours => Math.Round(TotalFocusMinutes / 60.0, 1);
    public int CompletedFocusSessions { get; set; }
    public int InterruptedFocusSessions { get; set; }
    public double CompletionRate => (CompletedFocusSessions + InterruptedFocusSessions) > 0
        ? Math.Round((double)CompletedFocusSessions / (CompletedFocusSessions + InterruptedFocusSessions) * 100.0, 1)
        : 0;

    public int TasksCompleted { get; set; }
    public int PlannedMinutes { get; set; }
    public double ActualMinutes { get; set; }

    public int CurrentStreakDays { get; set; }
    public int LongestStreakDays { get; set; }

    public List<DayProductivityMetric> DailyBreakdown { get; set; } = new();
    public List<ProjectFocusMetric> ProjectBreakdown { get; set; } = new();
}

public sealed class AnalyticsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static (DateTime StartLocal, DateTime EndLocal) ResolveCustomRange(
        DateTime? customStart,
        DateTime? customEnd,
        DateTime? referenceToday = null)
    {
        var today = referenceToday ?? DateTime.Today;
        var start = (customStart ?? today.AddDays(-7)).Date;
        var end = (customEnd ?? today).Date;

        if (end < start)
        {
            (start, end) = (end, start);
        }

        return (start, end.AddDays(1).AddTicks(-1));
    }

    public static (DateTime StartLocal, DateTime EndLocal) GetDateRange(
        TimeRangePreset preset,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        var today = DateTime.Today;

        return preset switch
        {
            TimeRangePreset.Today => (today, today.AddDays(1).AddTicks(-1)),
            TimeRangePreset.Yesterday => (today.AddDays(-1), today.AddTicks(-1)),
            TimeRangePreset.Last7Days => (today.AddDays(-6), today.AddDays(1).AddTicks(-1)),
            TimeRangePreset.Last30Days => (today.AddDays(-29), today.AddDays(1).AddTicks(-1)),
            TimeRangePreset.ThisWeek =>
            (
                today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1)), // Monday start
                today.AddDays(1).AddTicks(-1)
            ),
            TimeRangePreset.ThisMonth =>
            (
                new DateTime(today.Year, today.Month, 1),
                today.AddDays(1).AddTicks(-1)
            ),
            TimeRangePreset.Custom => ResolveCustomRange(customStart, customEnd, today),
            _ => (DateTime.MinValue, DateTime.MaxValue) // AllTime
        };
    }

    public ProductivitySummary GenerateSummary(
        IReadOnlyList<FocusSessionRecord> sessions,
        IReadOnlyList<TaskItem> tasks,
        TimeRangePreset preset,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        var (startLocal, endLocal) = GetDateRange(preset, customStart, customEnd);

        // Filter sessions in date range
        var inRangeSessions = sessions.Where(s =>
        {
            var l = s.StartLocal;
            return l >= startLocal && l <= endLocal;
        }).ToList();

        // Filter tasks completed in date range
        var inRangeTasksCompleted = tasks.Where(t =>
        {
            if (!t.IsCompleted || !t.CompletedAt.HasValue) return false;
            var l = TaskItem.ToLocalDate(t.CompletedAt.Value);
            return l >= startLocal.Date && l <= endLocal.Date;
        }).ToList();

        var focusSessions = inRangeSessions.Where(s => s.SessionType == FocusSessionType.Focus).ToList();
        var completedFocus = focusSessions.Where(s => s.Status == FocusSessionStatus.Completed).ToList();
        var interruptedFocus = focusSessions.Where(s => s.Status != FocusSessionStatus.Completed).ToList();

        double totalFocusMinutes = Math.Round(completedFocus.Sum(s => s.ActualSeconds) / 60.0, 1);

        // Planned vs Actual
        int plannedMinutes = inRangeTasksCompleted.Sum(t => t.EstimatedMinutes);
        double actualMinutes = inRangeTasksCompleted.Sum(t => t.TotalFocusMinutes);

        // Daily breakdown
        var dailyBreakdown = new List<DayProductivityMetric>();
        DateTime dayIterator = (preset == TimeRangePreset.AllTime)
            ? (inRangeSessions.Count > 0 ? inRangeSessions.Min(s => s.StartLocal).Date : DateTime.Today.AddDays(-6))
            : startLocal.Date;

        DateTime endDay = (preset == TimeRangePreset.AllTime)
            ? (inRangeSessions.Count > 0 ? inRangeSessions.Max(s => s.StartLocal).Date : DateTime.Today)
            : endLocal.Date;

        // Limit range if iterator is crazy large
        if ((endDay - dayIterator).TotalDays > 60)
        {
            dayIterator = endDay.AddDays(-59);
        }

        while (dayIterator <= endDay)
        {
            var currentDay = dayIterator;
            var daySessions = completedFocus.Where(s => s.StartLocal.Date == currentDay).ToList();
            var dayTasks = inRangeTasksCompleted.Where(t => t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == currentDay).ToList();

            dailyBreakdown.Add(new DayProductivityMetric
            {
                Date = currentDay,
                DayLabel = currentDay.ToString("MMM d"),
                FocusMinutes = Math.Round(daySessions.Sum(s => s.ActualSeconds) / 60.0, 1),
                CompletedSessions = daySessions.Count,
                CompletedTasks = dayTasks.Count
            });

            dayIterator = dayIterator.AddDays(1);
        }

        // Project breakdown
        var projectGroups = completedFocus
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Project) ? "Uncategorized" : s.Project.Trim())
            .Select(g =>
            {
                double mins = Math.Round(g.Sum(s => s.ActualSeconds) / 60.0, 1);
                return new ProjectFocusMetric
                {
                    ProjectName = g.Key,
                    FocusMinutes = mins,
                    Percentage = totalFocusMinutes > 0 ? Math.Round((mins / totalFocusMinutes) * 100.0, 1) : 0,
                    SessionCount = g.Count()
                };
            })
            .OrderByDescending(p => p.FocusMinutes)
            .ToList();

        var (currentStreak, longestStreak) = CalculateStreaks(sessions, tasks);

        return new ProductivitySummary
        {
            TotalFocusMinutes = totalFocusMinutes,
            CompletedFocusSessions = completedFocus.Count,
            InterruptedFocusSessions = interruptedFocus.Count,
            TasksCompleted = inRangeTasksCompleted.Count,
            PlannedMinutes = plannedMinutes,
            ActualMinutes = actualMinutes,
            CurrentStreakDays = currentStreak,
            LongestStreakDays = longestStreak,
            DailyBreakdown = dailyBreakdown,
            ProjectBreakdown = projectGroups
        };
    }

    public static (int CurrentStreak, int LongestStreak) CalculateStreaks(
        IReadOnlyList<FocusSessionRecord> sessions,
        IReadOnlyList<TaskItem> tasks)
    {
        // Set of active calendar dates (dates with at least 1 completed focus session OR completed task)
        var activeDates = new HashSet<DateTime>();

        foreach (var s in sessions)
        {
            if (s.SessionType == FocusSessionType.Focus && s.Status == FocusSessionStatus.Completed)
            {
                activeDates.Add(s.StartLocal.Date);
            }
        }

        foreach (var t in tasks)
        {
            if (t.IsCompleted && t.CompletedAt.HasValue)
            {
                activeDates.Add(TaskItem.ToLocalDate(t.CompletedAt.Value));
            }
        }

        if (activeDates.Count == 0)
        {
            return (0, 0);
        }

        var today = DateTime.Today;

        // Current streak calculation
        int currentStreak = 0;
        DateTime checkDate = today;

        if (activeDates.Contains(checkDate))
        {
            while (activeDates.Contains(checkDate))
            {
                currentStreak++;
                checkDate = checkDate.AddDays(-1);
            }
        }
        else if (activeDates.Contains(checkDate.AddDays(-1)))
        {
            // If nothing done today yet, yesterday's streak is still alive
            checkDate = checkDate.AddDays(-1);
            while (activeDates.Contains(checkDate))
            {
                currentStreak++;
                checkDate = checkDate.AddDays(-1);
            }
        }

        // Longest streak calculation
        var sortedDates = activeDates.OrderBy(d => d).ToList();
        int longestStreak = 0;
        int currentRun = 0;
        DateTime? previousDate = null;

        foreach (var date in sortedDates)
        {
            if (previousDate == null || date == previousDate.Value.AddDays(1))
            {
                currentRun++;
            }
            else
            {
                currentRun = 1;
            }

            if (currentRun > longestStreak)
            {
                longestStreak = currentRun;
            }

            previousDate = date;
        }

        return (currentStreak, Math.Max(longestStreak, currentStreak));
    }

    // ── Export Helpers ────────────────────────────────────────────────────────

    public static string ExportSessionsToCsv(IEnumerable<FocusSessionRecord> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID,StartLocal,EndLocal,Type,TargetMinutes,ActualSeconds,ActualMinutes,PausedSeconds,Status,TaskTitle,Project,Notes");

        foreach (var s in sessions)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "\"{0}\",\"{1:yyyy-MM-dd HH:mm:ss}\",\"{2:yyyy-MM-dd HH:mm:ss}\",\"{3}\",{4},{5:F1},{6:F1},{7:F1},\"{8}\",\"{9}\",\"{10}\",\"{11}\"",
                s.Id,
                s.StartLocal,
                s.EndLocal,
                s.SessionType,
                s.TargetMinutes,
                s.ActualSeconds,
                s.ActualMinutes,
                s.PausedSeconds,
                s.Status,
                EscapeCsv(s.TaskTitle),
                EscapeCsv(s.Project),
                EscapeCsv(s.Notes)));
        }

        return sb.ToString();
    }

    public static string ExportSessionsToJson(IEnumerable<FocusSessionRecord> sessions)
    {
        return JsonSerializer.Serialize(sessions, JsonOpts);
    }

    public static string ExportTasksToCsv(IEnumerable<TaskItem> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID,Title,Description,Status,Priority,DueDate,EstimatedMinutes,CompletedPomodoros,TotalFocusMinutes,Project,Tags,CreatedAt,CompletedAt");

        foreach (var t in tasks)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",{6},{7},{8},\"{9}\",\"{10}\",\"{11:yyyy-MM-dd HH:mm:ss}\",\"{12:yyyy-MM-dd HH:mm:ss}\"",
                t.Id,
                EscapeCsv(t.Title),
                EscapeCsv(t.Description),
                t.Status,
                t.Priority,
                t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : "",
                t.EstimatedMinutes,
                t.CompletedPomodoros,
                t.TotalFocusMinutes,
                EscapeCsv(t.Project),
                EscapeCsv(string.Join(";", t.Tags)),
                t.CreatedAt.ToLocalTime(),
                t.CompletedAt.HasValue ? t.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : ""));
        }

        return sb.ToString();
    }

    public static string ExportTasksToJson(IEnumerable<TaskItem> tasks)
    {
        return JsonSerializer.Serialize(tasks, JsonOpts);
    }

    public static string ExportFullBackupJson(
        IEnumerable<TaskItem> tasks,
        IEnumerable<FocusSessionRecord> sessions,
        AppSettings settings)
    {
        var doc = new ProductivityBackupDocument
        {
            Version = 1,
            ExportedAtUtc = DateTime.UtcNow,
            Tasks = tasks.ToList(),
            FocusHistory = sessions.ToList(),
            DailyFocusGoalMinutes = settings.DailyFocusGoalMinutes,
            WeeklyFocusGoalMinutes = settings.WeeklyFocusGoalMinutes,
            DailyTaskGoal = settings.DailyTaskGoal,
            WeeklyTaskGoal = settings.WeeklyTaskGoal
        };

        return JsonSerializer.Serialize(doc, JsonOpts);
    }

    public static List<GoalAchievementDay> CalculateGoalAchievements(
        IReadOnlyList<FocusSessionRecord> sessions,
        IReadOnlyList<TaskItem> tasks,
        AppSettings settings,
        int daysBack = 7)
    {
        var result = new List<GoalAchievementDay>();
        var today = DateTime.Today;

        for (int i = daysBack - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var daySessions = sessions.Where(s => s.SessionType == FocusSessionType.Focus &&
                                                 s.Status == FocusSessionStatus.Completed &&
                                                 s.StartLocal.Date == date).ToList();
            double dayFocus = Math.Round(daySessions.Sum(s => s.ActualSeconds) / 60.0, 1);
            int dayTasks = tasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == date);

            result.Add(new GoalAchievementDay
            {
                Date = date,
                FocusMinutes = dayFocus,
                FocusGoalMinutes = settings.DailyFocusGoalMinutes,
                TasksCompleted = dayTasks,
                TaskGoal = settings.DailyTaskGoal
            });
        }

        return result;
    }

    public static List<ActivityHeatmapCell> GenerateActivityHeatmap(
        IReadOnlyList<FocusSessionRecord> sessions,
        IReadOnlyList<TaskItem> tasks,
        int weeks = 12)
    {
        var result = new List<ActivityHeatmapCell>();
        var today = DateTime.Today;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var startMonday = today.AddDays(-daysSinceMonday).AddDays(-(weeks - 1) * 7);

        for (int w = 0; w < weeks; w++)
        {
            for (int d = 0; d < 7; d++)
            {
                var date = startMonday.AddDays(w * 7 + d);
                if (date > today) continue;

                var daySessions = sessions.Where(s => s.SessionType == FocusSessionType.Focus &&
                                                     s.Status == FocusSessionStatus.Completed &&
                                                     s.StartLocal.Date == date).ToList();
                double dayFocus = Math.Round(daySessions.Sum(s => s.ActualSeconds) / 60.0, 1);
                int dayTasks = tasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == date);

                int intensity = 0;
                if (dayFocus >= 100) intensity = 4;
                else if (dayFocus >= 50) intensity = 3;
                else if (dayFocus >= 25) intensity = 2;
                else if (dayFocus > 0 || dayTasks > 0) intensity = 1;

                result.Add(new ActivityHeatmapCell
                {
                    Date = date,
                    WeekIndex = w,
                    FocusMinutes = dayFocus,
                    IntensityLevel = intensity,
                    CompletedSessions = daySessions.Count,
                    CompletedTasks = dayTasks
                });
            }
        }

        return result;
    }

    private static string EscapeCsv(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        return val.Replace("\"", "\"\"");
    }
}

public sealed class ActivityHeatmapCell
{
    public DateTime Date { get; set; }
    public int WeekIndex { get; set; }
    public int DayOfWeekIndex => ((int)Date.DayOfWeek + 6) % 7;
    public double FocusMinutes { get; set; }
    public int IntensityLevel { get; set; }
    public int CompletedSessions { get; set; }
    public int CompletedTasks { get; set; }
    public string TooltipText => $"{Date:ddd, MMM d, yyyy}: {(int)FocusMinutes}m focus ({CompletedSessions} sessions, {CompletedTasks} tasks)";
}

public sealed class GoalAchievementDay
{
    public DateTime Date { get; set; }
    public string FormattedDate => Date.ToString("ddd, MMM d");
    public double FocusMinutes { get; set; }
    public int FocusGoalMinutes { get; set; }
    public bool FocusGoalAchieved => FocusMinutes >= FocusGoalMinutes && FocusGoalMinutes > 0;
    public int TasksCompleted { get; set; }
    public int TaskGoal { get; set; }
    public bool TaskGoalAchieved => TasksCompleted >= TaskGoal && TaskGoal > 0;
    public bool BothAchieved => FocusGoalAchieved && TaskGoalAchieved;
    public string FocusStatusDisplay => $"Focus: {(int)FocusMinutes}/{FocusGoalMinutes}m {(FocusGoalAchieved ? "✓" : "—")}";
    public string TaskStatusDisplay => $"Tasks: {TasksCompleted}/{TaskGoal} {(TaskGoalAchieved ? "✓" : "—")}";
    public string ResultBadge => BothAchieved ? "🎉 Both Met" : (FocusGoalAchieved || TaskGoalAchieved ? "⚡ Partial" : "— Missed");
    public string BadgeBrush => BothAchieved ? "#22C55E" : (FocusGoalAchieved || TaskGoalAchieved ? "#F97316" : "#8D7B9D");
    public string SummaryText => $"{Date:MMM d}: Focus {(int)FocusMinutes}/{FocusGoalMinutes}m {(FocusGoalAchieved ? "✅" : "❌")}, Tasks {TasksCompleted}/{TaskGoal} {(TaskGoalAchieved ? "✅" : "❌")}";
}

public sealed class ProductivityBackupDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exportedAtUtc")]
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tasks")]
    public List<TaskItem> Tasks { get; set; } = new();

    [JsonPropertyName("focusHistory")]
    public List<FocusSessionRecord> FocusHistory { get; set; } = new();

    [JsonPropertyName("dailyFocusGoalMinutes")]
    public int DailyFocusGoalMinutes { get; set; }

    [JsonPropertyName("weeklyFocusGoalMinutes")]
    public int WeeklyFocusGoalMinutes { get; set; }

    [JsonPropertyName("dailyTaskGoal")]
    public int DailyTaskGoal { get; set; }

    [JsonPropertyName("weeklyTaskGoal")]
    public int WeeklyTaskGoal { get; set; }
}
