using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public sealed class ProductivityHubTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tasksFile;
    private readonly string _historyFile;

    public ProductivityHubTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NekuHubTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tasksFile = Path.Combine(_tempDir, "tasks.json");
        _historyFile = Path.Combine(_tempDir, "focus_history.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void TaskItem_StatusAndIsCompleted_StaySynchronized()
    {
        var task = new TaskItem
        {
            Title = "Sync Test",
            Status = TaskItemStatus.Todo,
            IsCompleted = false
        };

        // When IsCompleted is set to true -> Status becomes Completed
        task.IsCompleted = true;
        Assert.Equal(TaskItemStatus.Completed, task.Status);

        // When IsCompleted is set to false -> Status becomes Todo
        task.IsCompleted = false;
        Assert.Equal(TaskItemStatus.Todo, task.Status);

        // When Status is set to Completed -> IsCompleted becomes true
        task.Status = TaskItemStatus.Completed;
        Assert.True(task.IsCompleted);

        // When Status is set to InProgress -> IsCompleted becomes false
        task.Status = TaskItemStatus.InProgress;
        Assert.False(task.IsCompleted);

        // When Status is set to Inbox -> IsCompleted remains false
        task.Status = TaskItemStatus.Inbox;
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void TaskItem_BackwardCompatibility_LoadsLegacyV1JsonGracefully()
    {
        // Legacy JSON without status, subtasks, recurrence, project, tags
        string legacyJson = """
        {
          "version": 1,
          "tasks": [
            {
              "id": "legacy-task-1",
              "title": "Legacy Task",
              "description": "Old schema task",
              "isCompleted": true,
              "priority": "High",
              "dueDate": "2026-10-18T10:00:00Z",
              "estimatedMinutes": 50,
              "completedPomodoros": 2
            },
            {
              "id": "legacy-task-2",
              "title": "Legacy Pending Task",
              "isCompleted": false,
              "priority": "Normal"
            }
          ]
        }
        """;

        File.WriteAllText(_tasksFile, legacyJson);
        var service = new TaskService(_tasksFile);

        Assert.Equal(2, service.Tasks.Count);

        var completed = service.Get("legacy-task-1");
        Assert.NotNull(completed);
        Assert.True(completed.IsCompleted);
        Assert.Equal(TaskItemStatus.Completed, completed.Status);
        Assert.Equal(TaskPriority.High, completed.Priority);
        Assert.NotNull(completed.Subtasks);
        Assert.Empty(completed.Subtasks);
        Assert.NotNull(completed.Tags);
        Assert.Equal(TaskRecurrenceRule.None, completed.RecurrenceRule);

        var pending = service.Get("legacy-task-2");
        Assert.NotNull(pending);
        Assert.False(pending.IsCompleted);
        Assert.Equal(TaskItemStatus.Todo, pending.Status);
        Assert.Equal(TaskPriority.Normal, pending.Priority);
    }

    [Fact]
    public void TaskRecurrence_CalculateNextDate_ComputesDailyAndWeeklyAccurately()
    {
        var baseDate = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Local);

        var nextDaily = TaskItem.CalculateNextRecurrenceDate(baseDate, TaskRecurrenceRule.Daily);
        Assert.Equal(new DateTime(2026, 10, 19), nextDaily.Date);

        var nextWeekly = TaskItem.CalculateNextRecurrenceDate(baseDate, TaskRecurrenceRule.Weekly);
        Assert.Equal(new DateTime(2026, 10, 25), nextWeekly.Date);
    }

    [Fact]
    public void TaskService_RecurringTaskCompletion_GeneratesNextOccurrenceDeterministically()
    {
        var service = new TaskService(_tasksFile);

        var task = service.Create(
            title: "Daily Standup",
            description: "Prepare status",
            priority: TaskPriority.Urgent,
            dueDate: new DateTime(2026, 10, 18),
            estimatedMinutes: 15,
            status: TaskItemStatus.Todo,
            project: "Work",
            tags: new[] { "daily", "meeting" },
            recurrence: TaskRecurrenceRule.Daily);

        service.AddSubtask(task.Id, "Review git log");
        service.AddSubtask(task.Id, "Draft notes");

        // Complete the recurring task
        bool completed = service.Complete(task.Id);
        Assert.True(completed);

        // There should now be 2 tasks: the completed one and the newly spawned next occurrence
        var tasks = service.Tasks;
        Assert.Equal(2, tasks.Count);

        var original = tasks.First(t => t.Id == task.Id);
        Assert.True(original.IsCompleted);
        Assert.Equal(TaskItemStatus.Completed, original.Status);
        // Original has recurrence removed to prevent duplicate generation on re-completion
        Assert.Equal(TaskRecurrenceRule.None, original.RecurrenceRule);

        var nextOccurrence = tasks.First(t => t.Id != task.Id);
        Assert.False(nextOccurrence.IsCompleted);
        Assert.Equal(TaskItemStatus.Todo, nextOccurrence.Status);
        Assert.Equal("Daily Standup", nextOccurrence.Title);
        Assert.Equal(TaskPriority.Urgent, nextOccurrence.Priority);
        Assert.Equal("Work", nextOccurrence.Project);
        Assert.Equal(TaskRecurrenceRule.Daily, nextOccurrence.RecurrenceRule);
        Assert.Equal(new DateTime(2026, 10, 19), nextOccurrence.DueDate?.Date);
        Assert.Equal(2, nextOccurrence.Subtasks.Count);
        Assert.All(nextOccurrence.Subtasks, s => Assert.False(s.IsCompleted));
    }

    [Fact]
    public void TaskService_SubtasksOperations_WorkCorrectly()
    {
        var service = new TaskService(_tasksFile);
        var task = service.Create("Subtask Container");

        Assert.True(service.AddSubtask(task.Id, "Step 1"));
        Assert.True(service.AddSubtask(task.Id, "Step 2"));

        var updated = service.Get(task.Id)!;
        Assert.Equal(2, updated.Subtasks.Count);
        Assert.Equal("0/2 subtasks", updated.SubtasksSummary);

        string step1Id = updated.Subtasks[0].Id;
        service.ToggleSubtask(task.Id, step1Id);
        Assert.True(updated.Subtasks[0].IsCompleted);
        Assert.Equal("1/2 subtasks", updated.SubtasksSummary);

        service.DeleteSubtask(task.Id, step1Id);
        Assert.Single(updated.Subtasks);
        Assert.Equal("Step 2", updated.Subtasks[0].Title);
    }

    [Fact]
    public void TaskService_ClearCompleted_RemovesOnlyCompletedTasks()
    {
        var service = new TaskService(_tasksFile);
        var t1 = service.Create("Task 1");
        var t2 = service.Create("Task 2");
        var t3 = service.Create("Task 3");

        service.Complete(t1.Id);
        service.Complete(t3.Id);

        int cleared = service.ClearCompleted();
        Assert.Equal(2, cleared);
        Assert.Single(service.Tasks);
        Assert.Equal(t2.Id, service.Tasks[0].Id);
    }

    [Fact]
    public void FocusHistoryService_RecordsSessions_AndRecoversFromCorruptFile()
    {
        var history = new FocusHistoryService(_historyFile);

        var record1 = new FocusSessionRecord
        {
            SessionType = FocusSessionType.Focus,
            TargetMinutes = 25,
            ActualSeconds = 1500,
            Status = FocusSessionStatus.Completed,
            TaskTitle = "Code Review",
            Project = "Neku"
        };

        var record2 = new FocusSessionRecord
        {
            SessionType = FocusSessionType.ShortBreak,
            TargetMinutes = 5,
            ActualSeconds = 300,
            Status = FocusSessionStatus.Completed
        };

        history.RecordSession(record1);
        history.RecordSession(record2);

        Assert.Equal(2, history.Sessions.Count);
        Assert.Equal(25, history.GetTodayFocusMinutes());
        Assert.Equal(1, history.GetTodayCompletedFocusCount());

        // Reload from disk
        var reloaded = new FocusHistoryService(_historyFile);
        Assert.Equal(2, reloaded.Sessions.Count);

        // Test corruption recovery
        File.WriteAllText(_historyFile, "{ malformed json: not valid }");
        var recoverHistory = new FocusHistoryService(_historyFile);
        Assert.Empty(recoverHistory.Sessions);
        Assert.NotNull(recoverHistory.LastCorruptBackupPath);
        Assert.True(File.Exists(recoverHistory.LastCorruptBackupPath));
    }

    [Fact]
    public void AnalyticsService_CalculatesStreaksAndSummaryAccurately()
    {
        var analytics = new AnalyticsService();
        var today = DateTime.Today;

        var sessions = new List<FocusSessionRecord>
        {
            new() { StartUtc = today.AddDays(-2).ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 1500, Project = "Dev" },
            new() { StartUtc = today.AddDays(-1).ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 1500, Project = "Dev" },
            new() { StartUtc = today.ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 3000, Project = "Review" },
            new() { StartUtc = today.ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Cancelled, ActualSeconds = 120, Project = "Review" }
        };

        var tasks = new List<TaskItem>
        {
            new() { Title = "Task A", IsCompleted = true, CompletedAt = today.AddDays(-2).ToUniversalTime(), EstimatedMinutes = 30, TotalFocusMinutes = 25 },
            new() { Title = "Task B", IsCompleted = true, CompletedAt = today.ToUniversalTime(), EstimatedMinutes = 60, TotalFocusMinutes = 50 }
        };

        var (currentStreak, longestStreak) = AnalyticsService.CalculateStreaks(sessions, tasks);
        Assert.Equal(3, currentStreak);
        Assert.True(longestStreak >= 3);

        var summary = analytics.GenerateSummary(sessions, tasks, TimeRangePreset.Last7Days);
        Assert.Equal(100.0, summary.TotalFocusMinutes); // (1500+1500+3000)/60 = 100m
        Assert.Equal(3, summary.CompletedFocusSessions);
        Assert.Equal(1, summary.InterruptedFocusSessions);
        Assert.Equal(75.0, summary.CompletionRate); // 3/4 = 75%
        Assert.Equal(2, summary.TasksCompleted);
        Assert.Equal(90, summary.PlannedMinutes); // 30 + 60
        Assert.Equal(75.0, summary.ActualMinutes); // 25 + 50

        Assert.Equal(2, summary.ProjectBreakdown.Count);
        Assert.Contains(summary.ProjectBreakdown, p => p.ProjectName == "Review" && p.FocusMinutes == 50.0);
        Assert.Contains(summary.ProjectBreakdown, p => p.ProjectName == "Dev" && p.FocusMinutes == 50.0);
    }

    [Fact]
    public void AnalyticsService_ExportCsvAndJson_ProducesValidFormattedOutputs()
    {
        var sessions = new List<FocusSessionRecord>
        {
            new()
            {
                Id = "sess-1",
                StartUtc = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 10, 18, 10, 25, 0, DateTimeKind.Utc),
                SessionType = FocusSessionType.Focus,
                TargetMinutes = 25,
                ActualSeconds = 1500,
                PausedSeconds = 0,
                Status = FocusSessionStatus.Completed,
                TaskTitle = "Code \"Testing\" Feature",
                Project = "PixelPaws"
            }
        };

        var tasks = new List<TaskItem>
        {
            new()
            {
                Id = "task-1",
                Title = "Feature \"A\"",
                Description = "Line 1\nLine 2",
                Status = TaskItemStatus.Todo,
                Priority = TaskPriority.High,
                EstimatedMinutes = 25,
                Project = "PixelPaws",
                Tags = new List<string> { "backend", "test" }
            }
        };

        // Export Sessions CSV
        string csvSessions = AnalyticsService.ExportSessionsToCsv(sessions);
        Assert.Contains("Code \"\"Testing\"\" Feature", csvSessions);
        Assert.Contains("PixelPaws", csvSessions);

        // Export Sessions JSON
        string jsonSessions = AnalyticsService.ExportSessionsToJson(sessions);
        Assert.Contains("sess-1", jsonSessions);
        var parsedSessions = JsonSerializer.Deserialize<List<FocusSessionRecord>>(jsonSessions);
        Assert.NotNull(parsedSessions);
        Assert.Single(parsedSessions);

        // Export Tasks CSV
        string csvTasks = AnalyticsService.ExportTasksToCsv(tasks);
        Assert.Contains("Feature \"\"A\"\"", csvTasks);
        Assert.Contains("backend;test", csvTasks);

        // Export Tasks JSON
        string jsonTasks = AnalyticsService.ExportTasksToJson(tasks);
        Assert.Contains("task-1", jsonTasks);
        var parsedTasks = JsonSerializer.Deserialize<List<TaskItem>>(jsonTasks);
        Assert.NotNull(parsedTasks);
        Assert.Single(parsedTasks);
    }

    [Fact]
    public void AppSettings_ProductivityDefaultsAndValidation_ClampSafely()
    {
        var settings = new AppSettings
        {
            DailyFocusGoalMinutes = -50,
            WeeklyFocusGoalMinutes = 0,
            DailyTaskGoal = -1,
            WeeklyTaskGoal = -10,
            LongBreakDurationMinutes = 0,
            LongBreakInterval = -3
        };

        settings.Validate();

        Assert.Equal(100, settings.DailyFocusGoalMinutes);
        Assert.Equal(500, settings.WeeklyFocusGoalMinutes);
        Assert.Equal(3, settings.DailyTaskGoal);
        Assert.Equal(15, settings.WeeklyTaskGoal);
        Assert.Equal(15, settings.LongBreakDurationMinutes);
        Assert.Equal(4, settings.LongBreakInterval);
    }

    [Fact]
    public void ProductivityReminderService_DuplicatePreventionAndSuppression()
    {
        var taskService = new TaskService(_tasksFile);
        var settings = new AppSettings
        {
            EnableTaskReminders = true,
            EnableQuietMode = false
        };
        var timer = new FocusTimerService();
        using var reminderService = new ProductivityReminderService(taskService, timer, settings);

        var today = DateTime.Today;
        taskService.Create(title: "Task Overdue", dueDate: today.AddDays(-1));
        taskService.Create(title: "Task Today", dueDate: today);
        taskService.Create(title: "Task Future", dueDate: today.AddDays(2));

        var alerts = new List<string>();
        reminderService.TaskDeadlineReminder += (_, msg) => alerts.Add(msg);

        // 1. Initial check: fires exactly 2 reminders (overdue and today)
        reminderService.CheckReminders();
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Contains("Task Overdue"));
        Assert.Contains(alerts, a => a.Contains("Task Today"));

        // 2. Second check on same calendar day: duplicate prevention ensures 0 new alerts
        reminderService.CheckReminders();
        Assert.Equal(2, alerts.Count);

        // 3. Clear cache and turn on Quiet Mode: suppression prevents alerts
        reminderService.ClearReminderCache();
        alerts.Clear();
        settings.EnableQuietMode = true;
        reminderService.CheckReminders();
        Assert.Empty(alerts);

        // 4. Quiet Mode off, but Presentation Mode delegate returns true: suppression prevents alerts
        settings.EnableQuietMode = false;
        reminderService.IsQuietOrPresenting = () => true;
        reminderService.CheckReminders();
        Assert.Empty(alerts);
    }

    [Fact]
    public void AnalyticsService_FullBackupJson_RoundtripsAllData()
    {
        var tasks = new List<TaskItem>
        {
            new() { Id = "t-1", Title = "Backup Task", Status = TaskItemStatus.Todo, Project = "Core", Tags = new List<string> { "p1" } }
        };

        var sessions = new List<FocusSessionRecord>
        {
            new() { Id = "s-1", SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 1500, TaskTitle = "Backup Task" }
        };

        var settings = new AppSettings
        {
            DailyFocusGoalMinutes = 120,
            WeeklyFocusGoalMinutes = 600,
            DailyTaskGoal = 5,
            WeeklyTaskGoal = 25
        };

        string json = AnalyticsService.ExportFullBackupJson(tasks, sessions, settings);
        Assert.NotNull(json);

        var doc = JsonSerializer.Deserialize<ProductivityBackupDocument>(json);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Version);
        Assert.Single(doc.Tasks);
        Assert.Equal("Backup Task", doc.Tasks[0].Title);
        Assert.Single(doc.FocusHistory);
        Assert.Equal("s-1", doc.FocusHistory[0].Id);
        Assert.Equal(120, doc.DailyFocusGoalMinutes);
        Assert.Equal(600, doc.WeeklyFocusGoalMinutes);
        Assert.Equal(5, doc.DailyTaskGoal);
        Assert.Equal(25, doc.WeeklyTaskGoal);
    }

    [Fact]
    public void FocusHistoryService_UpdateNotes_PersistsAndSaves()
    {
        var history = new FocusHistoryService(_historyFile);
        var record = new FocusSessionRecord
        {
            SessionType = FocusSessionType.Focus,
            Status = FocusSessionStatus.Completed,
            ActualSeconds = 1500,
            TaskTitle = "Note Test Task",
            Notes = "Initial note"
        };
        history.RecordSession(record);

        // Update note
        bool updated = history.UpdateNotes(record.Id, "Updated detailed session notes!");
        Assert.True(updated);

        // Reload from disk to verify persistence
        var reloaded = new FocusHistoryService(_historyFile);
        var fetched = reloaded.Sessions.FirstOrDefault(s => s.Id == record.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated detailed session notes!", fetched.Notes);
    }

    [Fact]
    public void AnalyticsService_ActivityHeatmap_Generates12WeeksCorrectly()
    {
        var today = DateTime.Today;
        var sessions = new List<FocusSessionRecord>
        {
            new() { StartUtc = today.ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 6000 }, // 100m -> intensity 4
            new() { StartUtc = today.AddDays(-2).ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 1800 } // 30m -> intensity 2
        };

        var tasks = new List<TaskItem>
        {
            new() { Title = "Done Task", IsCompleted = true, CompletedAt = today.AddDays(-5).ToUniversalTime() } // 1 task -> intensity 1
        };

        var cells = AnalyticsService.GenerateActivityHeatmap(sessions, tasks, weeks: 12);
        Assert.NotEmpty(cells);

        var todayCell = cells.FirstOrDefault(c => c.Date == today);
        Assert.NotNull(todayCell);
        Assert.Equal(4, todayCell.IntensityLevel);
        Assert.True(todayCell.FocusMinutes >= 100);

        var taskDayCell = cells.FirstOrDefault(c => c.Date == today.AddDays(-5));
        Assert.NotNull(taskDayCell);
        Assert.True(taskDayCell.IntensityLevel >= 1);
        Assert.Equal(1, taskDayCell.CompletedTasks);
    }

    [Fact]
    public void AnalyticsService_CalculateGoalAchievements_EvaluatesDailyGoals()
    {
        var today = DateTime.Today;
        var settings = new AppSettings
        {
            DailyFocusGoalMinutes = 50,
            DailyTaskGoal = 2
        };

        var sessions = new List<FocusSessionRecord>
        {
            new() { StartUtc = today.ToUniversalTime(), SessionType = FocusSessionType.Focus, Status = FocusSessionStatus.Completed, ActualSeconds = 3600 } // 60m focus (goal >= 50m)
        };

        var tasks = new List<TaskItem>
        {
            new() { Title = "Task 1", IsCompleted = true, CompletedAt = today.ToUniversalTime() },
            new() { Title = "Task 2", IsCompleted = true, CompletedAt = today.ToUniversalTime() } // 2 tasks (goal >= 2)
        };

        var achievements = AnalyticsService.CalculateGoalAchievements(sessions, tasks, settings, daysBack: 3);
        Assert.Equal(3, achievements.Count);

        var todayAchievement = achievements.FirstOrDefault(a => a.Date == today);
        Assert.NotNull(todayAchievement);
        Assert.True(todayAchievement.FocusGoalAchieved);
        Assert.True(todayAchievement.TaskGoalAchieved);
        Assert.True(todayAchievement.BothAchieved);
        Assert.Contains("Both Met", todayAchievement.ResultBadge);
    }

    [Fact]
    public void FocusTimerService_RestartRecovery_RestoresCycleAndSessionType()
    {
        var settings = new AppSettings
        {
            FocusDurationMinutes = 25,
            BreakDurationMinutes = 5,
            LongBreakDurationMinutes = 15,
            LongBreakInterval = 4
        };

        var timer = new FocusTimerService();

        // Start break session and complete 2 sessions in cycle
        timer.StartBreak(isLongBreak: false);
        timer.CompletedSessionsInCycle = 2;

        // Force SaveState
        timer.SaveState(settings);

        // Check AppSettings has persisted cycle and session type
        Assert.Equal(nameof(FocusSessionType.ShortBreak), settings.FocusTimerSessionType);
        Assert.Equal(2, settings.FocusCycleCompletedSessions);

        // Create new timer instance and load state (simulating app restart)
        var restoredTimer = new FocusTimerService();
        restoredTimer.LoadState(settings);

        Assert.Equal(2, restoredTimer.CompletedSessionsInCycle);
        Assert.Equal(FocusTimerState.Break, restoredTimer.State);
    }

    [Fact]
    public void AnalyticsService_ResolveCustomRange_SameDay()
    {
        var day = new DateTime(2026, 10, 18);
        var (start, end) = AnalyticsService.ResolveCustomRange(day, day);

        Assert.Equal(new DateTime(2026, 10, 18, 0, 0, 0), start);
        Assert.Equal(new DateTime(2026, 10, 18, 23, 59, 59).AddTicks(9999999), end);
        Assert.True(end > start);
        Assert.Equal(1, (end.Date - start.Date).Days + 1);
    }

    [Fact]
    public void AnalyticsService_ResolveCustomRange_MonthAndYearBoundaries()
    {
        // Boundary crossing December into January
        var startInput = new DateTime(2025, 12, 29);
        var endInput = new DateTime(2026, 1, 3);
        var (start, end) = AnalyticsService.ResolveCustomRange(startInput, endInput);

        Assert.Equal(new DateTime(2025, 12, 29, 0, 0, 0), start);
        Assert.Equal(new DateTime(2026, 1, 3, 23, 59, 59).AddTicks(9999999), end);
        Assert.Equal(6, (end.Date - start.Date).Days + 1);

        // Boundary crossing February into March (leap year check: 2028 is leap year)
        var febStart = new DateTime(2028, 2, 27);
        var marEnd = new DateTime(2028, 3, 2);
        var (fStart, mEnd) = AnalyticsService.ResolveCustomRange(febStart, marEnd);
        Assert.Equal(5, (mEnd.Date - fStart.Date).Days + 1); // 27, 28, 29, 1, 2 = 5 days
    }

    [Fact]
    public void AnalyticsService_ResolveCustomRange_InvertedRange_SwapsDates()
    {
        var earlier = new DateTime(2026, 5, 10);
        var later = new DateTime(2026, 5, 20);

        // Intentionally provide later as customStart and earlier as customEnd
        var (start, end) = AnalyticsService.ResolveCustomRange(later, earlier);

        Assert.Equal(new DateTime(2026, 5, 10, 0, 0, 0), start);
        Assert.Equal(new DateTime(2026, 5, 20, 23, 59, 59).AddTicks(9999999), end);
    }

    [Fact]
    public void AnalyticsService_ResolveCustomRange_NullFallback()
    {
        var fixedReference = new DateTime(2026, 8, 15);
        var (start, end) = AnalyticsService.ResolveCustomRange(null, null, fixedReference);

        Assert.Equal(new DateTime(2026, 8, 8, 0, 0, 0), start);
        Assert.Equal(new DateTime(2026, 8, 15, 23, 59, 59).AddTicks(9999999), end);
    }

    [Fact]
    public void AnalyticsService_CustomRange_FiltersSessionsAndTasksConsistently()
    {
        var targetDate = new DateTime(2026, 7, 10);
        var startRange = targetDate;
        var endRange = targetDate;

        // Session 1: exactly within range (10:00 AM local on target date)
        var inRangeSession = new FocusSessionRecord
        {
            StartUtc = targetDate.AddHours(10).ToUniversalTime(),
            SessionType = FocusSessionType.Focus,
            Status = FocusSessionStatus.Completed,
            ActualSeconds = 1800, // 30m
            Project = "Writing"
        };

        // Session 2: day before (outside range)
        var beforeSession = new FocusSessionRecord
        {
            StartUtc = targetDate.AddDays(-1).AddHours(23).ToUniversalTime(),
            SessionType = FocusSessionType.Focus,
            Status = FocusSessionStatus.Completed,
            ActualSeconds = 1500,
            Project = "Writing"
        };

        // Session 3: day after (outside range)
        var afterSession = new FocusSessionRecord
        {
            StartUtc = targetDate.AddDays(1).AddHours(1).ToUniversalTime(),
            SessionType = FocusSessionType.Focus,
            Status = FocusSessionStatus.Completed,
            ActualSeconds = 2400,
            Project = "Coding"
        };

        var sessions = new List<FocusSessionRecord> { inRangeSession, beforeSession, afterSession };

        var taskCompletedToday = new TaskItem
        {
            Title = "Today Task",
            IsCompleted = true,
            CompletedAt = targetDate.AddHours(14).ToUniversalTime(),
            EstimatedMinutes = 30
        };

        var taskCompletedYesterday = new TaskItem
        {
            Title = "Yesterday Task",
            IsCompleted = true,
            CompletedAt = targetDate.AddDays(-1).ToUniversalTime(),
            EstimatedMinutes = 60
        };

        var tasks = new List<TaskItem> { taskCompletedToday, taskCompletedYesterday };

        var analytics = new AnalyticsService();
        var summary = analytics.GenerateSummary(sessions, tasks, TimeRangePreset.Custom, startRange, endRange);

        // Only the in-range session should be counted
        Assert.Equal(1, summary.CompletedFocusSessions);
        Assert.Equal(30, summary.TotalFocusMinutes);
        Assert.Equal(1, summary.TasksCompleted);
        Assert.Single(summary.DailyBreakdown);
        Assert.Equal(30, summary.DailyBreakdown[0].FocusMinutes);
        Assert.Equal(1, summary.DailyBreakdown[0].CompletedTasks);
    }

    [Fact]
    public void AnalyticsService_CustomRange_EmptyRange_HandlesGracefullyWithoutThrowing()
    {
        var analytics = new AnalyticsService();
        var summary = analytics.GenerateSummary(
            new List<FocusSessionRecord>(),
            new List<TaskItem>(),
            TimeRangePreset.Custom,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 3));

        Assert.Equal(0, summary.TotalFocusMinutes);
        Assert.Equal(0, summary.CompletedFocusSessions);
        Assert.Equal(0, summary.TasksCompleted);
        Assert.Equal(0, summary.CompletionRate);
        Assert.Equal(3, summary.DailyBreakdown.Count);
        Assert.All(summary.DailyBreakdown, day =>
        {
            Assert.Equal(0, day.FocusMinutes);
            Assert.Equal(0, day.CompletedSessions);
            Assert.Equal(0, day.CompletedTasks);
        });
    }

    [Fact]
    public void ProductivityReminderService_IndependentToggles_OverdueDisabled_UpcomingFires()
    {
        var taskService = new TaskService(_tasksFile);
        var timer = new FocusTimerService();
        var settings = new AppSettings
        {
            EnableUpcomingTaskReminders = true,
            EnableOverdueTaskReminders = false,
            EnableQuietMode = false
        };

        var today = DateTime.Today;
        var overdueTask = taskService.Create(
            title: "Fix critical bug",
            dueDate: today.AddDays(-2).ToUniversalTime(),
            status: TaskItemStatus.Todo);
        var todayTask = taskService.Create(
            title: "Submit daily report",
            dueDate: today.ToUniversalTime(),
            status: TaskItemStatus.Todo);

        using var reminderService = new ProductivityReminderService(
            taskService, timer, settings, Dispatcher.CurrentDispatcher);

        var remindedTasks = new List<string>();
        reminderService.TaskDeadlineReminder += (task, msg) => remindedTasks.Add(task.Id);

        reminderService.CheckReminders();

        Assert.Contains(todayTask.Id, remindedTasks);
        Assert.DoesNotContain(overdueTask.Id, remindedTasks);
    }

    [Fact]
    public void ProductivityReminderService_IndependentToggles_UpcomingDisabled_OverdueFires()
    {
        var taskService = new TaskService(_tasksFile);
        var timer = new FocusTimerService();
        var settings = new AppSettings
        {
            EnableUpcomingTaskReminders = false,
            EnableOverdueTaskReminders = true,
            EnableQuietMode = false
        };

        var today = DateTime.Today;
        var overdueTask = taskService.Create(
            title: "Deploy release",
            dueDate: today.AddDays(-3).ToUniversalTime(),
            status: TaskItemStatus.Todo);
        var todayTask = taskService.Create(
            title: "Review pull request",
            dueDate: today.ToUniversalTime(),
            status: TaskItemStatus.Todo);

        using var reminderService = new ProductivityReminderService(
            taskService, timer, settings, Dispatcher.CurrentDispatcher);

        var remindedTasks = new List<string>();
        reminderService.TaskDeadlineReminder += (task, msg) => remindedTasks.Add(task.Id);

        reminderService.CheckReminders();

        Assert.Contains(overdueTask.Id, remindedTasks);
        Assert.DoesNotContain(todayTask.Id, remindedTasks);
    }

    [Fact]
    public void ProductivityReminderService_Suppression_QuietModeAndPresentationMode()
    {
        var taskService = new TaskService(_tasksFile);
        var timer = new FocusTimerService();
        var settings = new AppSettings
        {
            EnableUpcomingTaskReminders = true,
            EnableOverdueTaskReminders = true,
            EnableQuietMode = true // Quiet mode active
        };

        var today = DateTime.Today;
        taskService.Create(title: "Overdue task", dueDate: today.AddDays(-1).ToUniversalTime(), status: TaskItemStatus.Todo);
        taskService.Create(title: "Today task", dueDate: today.ToUniversalTime(), status: TaskItemStatus.Todo);

        using var reminderService = new ProductivityReminderService(
            taskService, timer, settings, Dispatcher.CurrentDispatcher);

        int remindedCount = 0;
        reminderService.TaskDeadlineReminder += (_, _) => remindedCount++;

        // 1. Suppressed by EnableQuietMode
        reminderService.CheckReminders();
        Assert.Equal(0, remindedCount);

        // 2. Unset QuietMode, but set IsQuietOrPresenting (e.g. Fullscreen app / presentation)
        settings.EnableQuietMode = false;
        reminderService.IsQuietOrPresenting = () => true;

        reminderService.CheckReminders();
        Assert.Equal(0, remindedCount);

        // 3. Clear presentation mode -> now it triggers
        reminderService.IsQuietOrPresenting = () => false;
        reminderService.CheckReminders();
        Assert.Equal(2, remindedCount);
    }

    [Fact]
    public void ProductivityReminderService_DuplicateSuppression_AlertsAtMostOncePerDay()
    {
        var taskService = new TaskService(_tasksFile);
        var timer = new FocusTimerService();
        var settings = new AppSettings
        {
            EnableUpcomingTaskReminders = true,
            EnableOverdueTaskReminders = true,
            EnableQuietMode = false
        };

        var today = DateTime.Today;
        taskService.Create(title: "Daily standup", dueDate: today.ToUniversalTime(), status: TaskItemStatus.Todo);

        using var reminderService = new ProductivityReminderService(
            taskService, timer, settings, Dispatcher.CurrentDispatcher);

        int alertCount = 0;
        reminderService.TaskDeadlineReminder += (_, _) => alertCount++;

        reminderService.CheckReminders();
        Assert.Equal(1, alertCount);

        // Subsequent check on same calendar day should be suppressed
        reminderService.CheckReminders();
        Assert.Equal(1, alertCount);

        // Cache clear simulates new day / fresh start
        reminderService.ClearReminderCache();
        reminderService.CheckReminders();
        Assert.Equal(2, alertCount);
    }

    [Fact]
    public void AppSettings_IndependentReminderSettings_PersistAndIsolateAcrossRestarts()
    {
        var settings = new AppSettings
        {
            EnableUpcomingTaskReminders = false,
            EnableOverdueTaskReminders = true,
            EnableFocusCompletedReminders = false,
            EnableBreakCompletedReminders = true
        };

        string json = JsonSerializer.Serialize(settings, SettingsService.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.Options);

        Assert.NotNull(restored);
        Assert.False(restored.EnableUpcomingTaskReminders);
        Assert.True(restored.EnableOverdueTaskReminders);
        Assert.False(restored.EnableFocusCompletedReminders);
        Assert.True(restored.EnableBreakCompletedReminders);
    }

    [Fact]
    public void DashboardWindow_InitializesAndSwitchesTabsWithoutExceptions()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_tasksFile);
                var timer = new FocusTimerService();
                var history = new FocusHistoryService(_historyFile);
                var settings = new AppSettings();

                var dashboard = new DesktopPet.UI.DashboardWindow(taskService, timer, history, settings);
                Assert.NotNull(dashboard);

                // Verify all tabs can be selected without any XAML or binding exceptions
                dashboard.SelectTab("Overview");
                dashboard.SelectTab("Tasks");
                dashboard.SelectTab("Focus");
                dashboard.SelectTab("History");
                dashboard.SelectTab("Insights");
                dashboard.SelectTab("Goals");

                dashboard.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow initialization timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void TasksWindow_InitializesAndSwitchesTabsWithoutExceptions()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_tasksFile);
                var timer = new FocusTimerService();
                var settings = new AppSettings();

                var tasksWindow = new DesktopPet.UI.TasksWindow(taskService, timer, settings);
                Assert.NotNull(tasksWindow);

                tasksWindow.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "TasksWindow initialization timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_RendersAllViewsToBitmapsWithoutErrors()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_tasksFile);
                var timer = new FocusTimerService();
                var history = new FocusHistoryService(_historyFile);
                var settings = new AppSettings();

                taskService.Create(title: "Review productivity polish", project: "Neku", dueDate: DateTime.Today);
                history.RecordSession(new FocusSessionRecord
                {
                    SessionType = FocusSessionType.Focus,
                    TargetMinutes = 25,
                    ActualSeconds = 1500,
                    Status = FocusSessionStatus.Completed,
                    TaskTitle = "Review productivity polish",
                    Project = "Neku",
                    StartUtc = DateTime.UtcNow.AddMinutes(-25),
                    EndUtc = DateTime.UtcNow
                });

                var dashboard = new DesktopPet.UI.DashboardWindow(taskService, timer, history, settings);
                dashboard.Width = 960;
                dashboard.Height = 640;

                string scratchDir = @"C:\Users\amvbl\.gemini\antigravity\brain\c3f88a8b-28be-4879-94fd-b66f4fb7d2f1\scratch";
                Directory.CreateDirectory(scratchDir);

                var content = (System.Windows.FrameworkElement)dashboard.Content;

                string[] tabs = ["Overview", "Tasks", "Focus", "History", "Insights", "Goals"];
                foreach (var tab in tabs)
                {
                    dashboard.SelectTab(tab);
                    content.Measure(new System.Windows.Size(960, 640));
                    content.Arrange(new System.Windows.Rect(0, 0, 960, 640));
                    content.UpdateLayout();

                    var rtb = new RenderTargetBitmap(960, 640, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(content);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    string file = Path.Combine(scratchDir, $"dashboard_{tab.ToLowerInvariant()}.png");
                    using var stream = File.Create(file);
                    encoder.Save(stream);
                }

                dashboard.Close();

                var tasksWindow = new DesktopPet.UI.TasksWindow(taskService, timer, settings);
                tasksWindow.Width = 740;
                tasksWindow.Height = 500;
                var tasksContent = (System.Windows.FrameworkElement)tasksWindow.Content;
                tasksContent.Measure(new System.Windows.Size(740, 500));
                tasksContent.Arrange(new System.Windows.Rect(0, 0, 740, 500));
                tasksContent.UpdateLayout();
                var rtbTasks = new RenderTargetBitmap(740, 500, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbTasks.Render(tasksContent);
                var encTasks = new PngBitmapEncoder();
                encTasks.Frames.Add(BitmapFrame.Create(rtbTasks));
                using (var stream = File.Create(Path.Combine(scratchDir, "tasks_window.png")))
                {
                    encTasks.Save(stream);
                }
                tasksWindow.Close();

                var cal = new System.Windows.Controls.Calendar();
                cal.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Neku;component/UI/ProductivityTheme.xaml", UriKind.Absolute)
                });
                cal.Style = (System.Windows.Style)cal.FindResource(typeof(System.Windows.Controls.Calendar));
                var container = new System.Windows.Controls.Border
                {
                    Background = (System.Windows.Media.Brush)cal.FindResource("ThemeBgBrush"),
                    Child = cal,
                    Width = 240,
                    Height = 240
                };
                container.Measure(new System.Windows.Size(240, 240));
                container.Arrange(new System.Windows.Rect(0, 0, 240, 240));
                container.UpdateLayout();
                var rtbCal = new RenderTargetBitmap(240, 240, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbCal.Render(container);
                var encCal = new PngBitmapEncoder();
                encCal.Frames.Add(BitmapFrame.Create(rtbCal));
                using (var stream = File.Create(Path.Combine(scratchDir, "calendar_render.png")))
                {
                    encCal.Save(stream);
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(15000);

        Assert.True(finished, "Rendering dashboard views timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void Dashboard_ComprehensiveInteractiveUiRegression_ExecutesAllInteractionsAndRenders()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tempTaskFile = Path.Combine(Path.GetTempPath(), $"tasks_regress_{Guid.NewGuid():N}.json");
                var tempHistoryFile = Path.Combine(Path.GetTempPath(), $"history_regress_{Guid.NewGuid():N}.json");
                var tempNotesFile = Path.Combine(Path.GetTempPath(), $"notes_regress_{Guid.NewGuid():N}.json");

                var taskService = new TaskService(tempTaskFile);
                var createdTask = taskService.Create(
                    title: "Verify UI polish regression",
                    description: "Test description",
                    priority: TaskPriority.High,
                    dueDate: DateTime.Today,
                    estimatedMinutes: 50,
                    status: TaskItemStatus.Todo,
                    project: "Testing",
                    tags: ["ui", "dark"]);
                taskService.AddSubtask(createdTask.Id, "Subtask 1: ComboBox check");
                taskService.AddSubtask(createdTask.Id, "Subtask 2: DatePicker check");

                var timer = new FocusTimerService();
                var history = new FocusHistoryService(tempHistoryFile);
                history.RecordSession(new FocusSessionRecord
                {
                    SessionType = FocusSessionType.Focus,
                    TargetMinutes = 25,
                    ActualSeconds = 1500,
                    Status = FocusSessionStatus.Completed,
                    TaskTitle = "Verify UI polish regression",
                    Project = "Testing",
                    StartUtc = DateTime.UtcNow.AddMinutes(-25),
                    EndUtc = DateTime.UtcNow,
                    Notes = "Initial session note"
                });

                var settings = new AppSettings();
                var settingsService = new SettingsService();
                var notesService = new NotesService(tempNotesFile);
                notesService.Create("Regression Test Note", "Testing QuickNotes styling and consistency.");

                string scratchDir = @"C:\Users\amvbl\.gemini\antigravity\brain\c3f88a8b-28be-4879-94fd-b66f4fb7d2f1\scratch";
                Directory.CreateDirectory(scratchDir);

                // 1. Dashboard Window Full Interactive Testing
                var dashboard = new DesktopPet.UI.DashboardWindow(taskService, timer, history, settings);
                dashboard.Width = 960;
                dashboard.Height = 640;
                var dashContent = (System.Windows.FrameworkElement)dashboard.Content;
                dashContent.Measure(new System.Windows.Size(960, 640));
                dashContent.Arrange(new System.Windows.Rect(0, 0, 960, 640));
                dashContent.UpdateLayout();

                // (a) Overview View
                dashboard.SelectTab("Overview");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.OverviewView.Visibility);

                // (b) Tasks View Interactions
                dashboard.SelectTab("Tasks");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.TasksView.Visibility);

                // Test Filter Tabs
                dashboard.TaskTabToday.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabUpcoming.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabInbox.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabInProgress.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabOverdue.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabDone.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TaskTabAll.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                // Test Search Filter
                dashboard.TaskSearchBox.Text = "polish";
                dashboard.TaskSearchBox.Text = "";

                // Test Toolbar Combos
                dashboard.TaskProjectCombo.IsDropDownOpen = true;
                dashboard.TaskProjectCombo.IsDropDownOpen = false;
                dashboard.TaskTagCombo.IsDropDownOpen = true;
                dashboard.TaskTagCombo.IsDropDownOpen = false;
                dashboard.TaskSortCombo.IsDropDownOpen = true;
                dashboard.TaskSortCombo.SelectedIndex = 1;
                dashboard.TaskSortCombo.IsDropDownOpen = false;

                // Select Task & Test Detail Panel
                dashboard.TasksListBox.SelectedIndex = 0;
                Assert.NotNull(dashboard.TasksListBox.SelectedItem);
                Assert.NotNull(dashboard.DetailDueDatePicker);
                dashboard.DetailDueDatePicker.IsDropDownOpen = true;
                dashboard.DetailDueDatePicker.SelectedDate = DateTime.Today.AddDays(3);
                dashboard.DetailDueDatePicker.IsDropDownOpen = false;

                dashboard.DetailPriorityCombo.IsDropDownOpen = true;
                dashboard.DetailPriorityCombo.SelectedIndex = 3; // Urgent
                dashboard.DetailPriorityCombo.IsDropDownOpen = false;

                dashboard.DetailRecurrenceCombo.IsDropDownOpen = true;
                dashboard.DetailRecurrenceCombo.SelectedIndex = 0; // None
                dashboard.DetailRecurrenceCombo.IsDropDownOpen = false;

                dashboard.DetailEstMinutesCombo.IsDropDownOpen = true;
                dashboard.DetailEstMinutesCombo.SelectedIndex = 2; // 50m
                dashboard.DetailEstMinutesCombo.IsDropDownOpen = false;

                // Checkbox toggle: mark completed
                dashboard.DetailCompleteCheck.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(createdTask.IsCompleted);

                // Switch to Done tab and uncheck to reopen
                dashboard.TaskTabDone.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TasksListBox.SelectedIndex = 0;
                dashboard.DetailCompleteCheck.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(createdTask.IsCompleted);

                // Switch back to All tab and add subtask
                dashboard.TaskTabAll.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dashboard.TasksListBox.SelectedIndex = 0;
                dashboard.DetailNewSubtaskBox.Text = "Subtask 3: Automated regression test";
                dashboard.DetailAddSubtaskBtn.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                // (c) Focus View Interactions
                dashboard.SelectTab("Focus");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.FocusView.Visibility);

                dashboard.FocusTaskSelectorCombo.IsDropDownOpen = true;
                dashboard.FocusTaskSelectorCombo.IsDropDownOpen = false;

                dashboard.FocusAutoBreakCheck.IsChecked = true;
                dashboard.FocusAutoFocusCheck.IsChecked = true;
                dashboard.FocusNotifyFocusCheck.IsChecked = false;
                dashboard.FocusNotifyBreakCheck.IsChecked = false;
                dashboard.FocusNotifyUpcomingCheck.IsChecked = false;
                dashboard.FocusNotifyOverdueCheck.IsChecked = false;

                dashboard.FocusNotifyFocusCheck.IsChecked = true;
                dashboard.FocusNotifyBreakCheck.IsChecked = true;
                dashboard.FocusNotifyUpcomingCheck.IsChecked = true;
                dashboard.FocusNotifyOverdueCheck.IsChecked = true;

                // (d) History View Interactions
                dashboard.SelectTab("History");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.HistoryView.Visibility);

                dashboard.HistoryRangeCombo.IsDropDownOpen = true;
                dashboard.HistoryRangeCombo.SelectedIndex = 0;
                dashboard.HistoryRangeCombo.IsDropDownOpen = false;

                dashboard.HistoryTypeCombo.IsDropDownOpen = true;
                dashboard.HistoryTypeCombo.SelectedIndex = 0;
                dashboard.HistoryTypeCombo.IsDropDownOpen = false;

                dashboard.HistoryStatusCombo.IsDropDownOpen = true;
                dashboard.HistoryStatusCombo.SelectedIndex = 0;
                dashboard.HistoryStatusCombo.IsDropDownOpen = false;

                dashboard.HistoryDataGrid.SelectedIndex = 0;
                dashboard.HistoryNotesTextBox.Text = "Updated focus session note via test.";
                dashboard.HistorySaveNotesBtn.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                // (e) Insights View Interactions
                dashboard.SelectTab("Insights");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.InsightsView.Visibility);

                dashboard.InsightsRangeCombo.IsDropDownOpen = true;
                dashboard.InsightsRangeCombo.SelectedIndex = 1;
                dashboard.InsightsRangeCombo.IsDropDownOpen = false;

                // (f) Goals View Interactions
                dashboard.SelectTab("Goals");
                Assert.Equal(System.Windows.Visibility.Visible, dashboard.GoalsView.Visibility);
                dashboard.GoalsDailyFocusTargetBox.Text = "120";

                // (g) Test Window Resizing (Min: 780x520, Standard: 960x640, Large: 1280x800)
                dashboard.SelectTab("Overview");
                dashContent.Measure(new System.Windows.Size(780, 520));
                dashContent.Arrange(new System.Windows.Rect(0, 0, 780, 520));
                dashContent.UpdateLayout();
                var rtbMin = new RenderTargetBitmap(780, 520, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbMin.Render(dashContent);
                var encMin = new PngBitmapEncoder();
                encMin.Frames.Add(BitmapFrame.Create(rtbMin));
                using (var stream = File.Create(Path.Combine(scratchDir, "dashboard_min_size.png")))
                {
                    encMin.Save(stream);
                }

                // High DPI Simulation (144 DPI = 150% scaling)
                dashContent.Measure(new System.Windows.Size(960, 640));
                dashContent.Arrange(new System.Windows.Rect(0, 0, 960, 640));
                dashContent.UpdateLayout();
                var rtbDpi = new RenderTargetBitmap(1440, 960, 144, 144, System.Windows.Media.PixelFormats.Pbgra32);
                rtbDpi.Render(dashContent);

                dashboard.Close();

                // 2. Standalone TasksWindow
                var tasksWindow = new DesktopPet.UI.TasksWindow(taskService, timer, settings);
                tasksWindow.Width = 740;
                tasksWindow.Height = 500;
                var twContent = (System.Windows.FrameworkElement)tasksWindow.Content;
                twContent.Measure(new System.Windows.Size(740, 500));
                twContent.Arrange(new System.Windows.Rect(0, 0, 740, 500));
                twContent.UpdateLayout();
                tasksWindow.Close();

                // 3. SettingsWindow
                var settingsWindow = new DesktopPet.UI.SettingsWindow(settings, settingsService);
                settingsWindow.Width = 440;
                settingsWindow.Height = 640;
                var swContent = (System.Windows.FrameworkElement)settingsWindow.Content;
                swContent.Measure(new System.Windows.Size(440, 640));
                swContent.Arrange(new System.Windows.Rect(0, 0, 440, 640));
                swContent.UpdateLayout();
                var rtbSettings = new RenderTargetBitmap(440, 640, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbSettings.Render(swContent);
                var encSettings = new PngBitmapEncoder();
                encSettings.Frames.Add(BitmapFrame.Create(rtbSettings));
                using (var stream = File.Create(Path.Combine(scratchDir, "settings_window.png")))
                {
                    encSettings.Save(stream);
                }
                settingsWindow.Close();

                // 4. QuickNotesWindow
                var notesWindow = new DesktopPet.UI.QuickNotesWindow(notesService);
                notesWindow.Width = 540;
                notesWindow.Height = 390;
                var nwContent = (System.Windows.FrameworkElement)notesWindow.Content;
                nwContent.Measure(new System.Windows.Size(540, 390));
                nwContent.Arrange(new System.Windows.Rect(0, 0, 540, 390));
                nwContent.UpdateLayout();
                var rtbNotes = new RenderTargetBitmap(540, 390, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbNotes.Render(nwContent);
                var encNotes = new PngBitmapEncoder();
                encNotes.Frames.Add(BitmapFrame.Create(rtbNotes));
                using (var stream = File.Create(Path.Combine(scratchDir, "quick_notes_window.png")))
                {
                    encNotes.Save(stream);
                }
                notesWindow.Close();

                // 5. DeveloperEventsWindow
                var devWindow = new DesktopPet.UI.DeveloperEventsWindow(null, null!, settings, settingsService);
                devWindow.Width = 440;
                devWindow.Height = 360;
                var devContent = (System.Windows.FrameworkElement)devWindow.Content;
                devContent.Measure(new System.Windows.Size(440, 360));
                devContent.Arrange(new System.Windows.Rect(0, 0, 440, 360));
                devContent.UpdateLayout();
                var rtbDev = new RenderTargetBitmap(440, 360, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbDev.Render(devContent);
                var encDev = new PngBitmapEncoder();
                encDev.Frames.Add(BitmapFrame.Create(rtbDev));
                using (var stream = File.Create(Path.Combine(scratchDir, "developer_events_window.png")))
                {
                    encDev.Save(stream);
                }
                devWindow.Close();

                // 6. ComboBox Dropdown Open Visual Rendering
                var testCombo = new System.Windows.Controls.ComboBox();
                testCombo.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Neku;component/UI/ProductivityTheme.xaml", UriKind.Absolute)
                });
                testCombo.Style = (System.Windows.Style)testCombo.FindResource(typeof(System.Windows.Controls.ComboBox));
                testCombo.Items.Add("Low Priority");
                testCombo.Items.Add("Normal Priority");
                testCombo.Items.Add("High Priority");
                testCombo.Items.Add("Urgent 🔥");
                testCombo.SelectedIndex = 1;

                var comboBorder = new System.Windows.Controls.Border
                {
                    Background = (System.Windows.Media.Brush)testCombo.FindResource("ThemePopupBgBrush"),
                    BorderBrush = (System.Windows.Media.Brush)testCombo.FindResource("ThemeInputBorderBrush"),
                    BorderThickness = new System.Windows.Thickness(1),
                    CornerRadius = new System.Windows.CornerRadius(6),
                    Padding = new System.Windows.Thickness(4),
                    Width = 160
                };
                var itemsStack = new System.Windows.Controls.StackPanel();
                foreach (var item in testCombo.Items)
                {
                    var cbItem = new System.Windows.Controls.ComboBoxItem { Content = item };
                    cbItem.Style = (System.Windows.Style)testCombo.FindResource(typeof(System.Windows.Controls.ComboBoxItem));
                    if (item.ToString() == "Normal Priority") cbItem.IsSelected = true;
                    itemsStack.Children.Add(cbItem);
                }
                comboBorder.Child = itemsStack;
                comboBorder.Measure(new System.Windows.Size(160, 160));
                comboBorder.Arrange(new System.Windows.Rect(0, 0, 160, 160));
                comboBorder.UpdateLayout();
                var rtbCombo = new RenderTargetBitmap(160, 160, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbCombo.Render(comboBorder);
                var encCombo = new PngBitmapEncoder();
                encCombo.Frames.Add(BitmapFrame.Create(rtbCombo));
                using (var stream = File.Create(Path.Combine(scratchDir, "combobox_popup.png")))
                {
                    encCombo.Save(stream);
                }

                // Cleanup
                try { if (File.Exists(tempTaskFile)) File.Delete(tempTaskFile); } catch { }
                try { if (File.Exists(tempHistoryFile)) File.Delete(tempHistoryFile); } catch { }
                try { if (File.Exists(tempNotesFile)) File.Delete(tempNotesFile); } catch { }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(20000);

        Assert.True(finished, "Interactive regression test timed out");
        Assert.Null(threadEx);
    }
}
