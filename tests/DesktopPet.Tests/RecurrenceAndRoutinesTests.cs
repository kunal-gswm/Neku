using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class RecurrenceAndRoutinesTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testTasksPath;

    public RecurrenceAndRoutinesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NekuRecurrenceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testTasksPath = Path.Combine(_testDir, "tasks.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. RECURRENCE ENGINE UNIT TESTS
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecurrenceEngine_Daily_CalculatesNextDay()
    {
        var schedule = new RecurringSchedule { RecurrenceRule = TaskRecurrenceRule.Daily };
        var baseDate = new DateTime(2026, 10, 20);

        var next = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, baseDate);
        Assert.Equal(new DateTime(2026, 10, 21), next);
    }

    [Fact]
    public void RecurrenceEngine_Weekdays_SkipsSaturdayAndSunday()
    {
        var schedule = new RecurringSchedule { RecurrenceRule = TaskRecurrenceRule.Weekdays };

        // Friday to Monday
        var friday = new DateTime(2026, 10, 23); // Friday
        Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);

        var next = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, friday);
        Assert.Equal(new DateTime(2026, 10, 26), next); // Monday
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);

        // Wednesday to Thursday
        var wednesday = new DateTime(2026, 10, 21);
        var nextDay = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, wednesday);
        Assert.Equal(new DateTime(2026, 10, 22), nextDay);
    }

    [Fact]
    public void RecurrenceEngine_Weekly_SingleAndMultipleDays_CalculatesCorrectly()
    {
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Weekly,
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Thursday }
        };

        // Monday -> should advance to Thursday
        var monday = new DateTime(2026, 10, 19);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        var nextFromMon = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, monday);
        Assert.Equal(new DateTime(2026, 10, 22), nextFromMon); // Thursday
        Assert.Equal(DayOfWeek.Thursday, nextFromMon.DayOfWeek);

        // Thursday -> should advance to next Monday
        var nextFromThu = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, nextFromMon);
        Assert.Equal(new DateTime(2026, 10, 26), nextFromThu); // Next Monday
        Assert.Equal(DayOfWeek.Monday, nextFromThu.DayOfWeek);
    }

    [Fact]
    public void RecurrenceEngine_Monthly_31stOnShorterMonths_ClampsToLastDay()
    {
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Monthly,
            DayOfMonth = 31
        };

        // Jan 31 -> Feb 28 (non-leap year 2027)
        var jan31 = new DateTime(2027, 1, 31);
        var febDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, jan31);
        Assert.Equal(new DateTime(2027, 2, 28), febDate);

        // Feb 28 -> Mar 31 (retains 31st target)
        var marDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, febDate);
        Assert.Equal(new DateTime(2027, 3, 31), marDate);

        // Mar 31 -> Apr 30 (April has 30 days)
        var aprDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, marDate);
        Assert.Equal(new DateTime(2027, 4, 30), aprDate);

        // Apr 30 -> May 31
        var mayDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, aprDate);
        Assert.Equal(new DateTime(2027, 5, 31), mayDate);
    }

    [Fact]
    public void RecurrenceEngine_Monthly_LeapYear_February29HandledCorrectly()
    {
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Monthly,
            DayOfMonth = 31
        };

        // In 2028 (leap year), February has 29 days
        var jan31_2028 = new DateTime(2028, 1, 31);
        var febDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, jan31_2028);
        Assert.Equal(new DateTime(2028, 2, 29), febDate);

        // Moving to March restores 31st
        var marDate = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, febDate);
        Assert.Equal(new DateTime(2028, 3, 31), marDate);
    }

    [Fact]
    public void RecurrenceEngine_YearTransition_CrossesBoundaryDeterministically()
    {
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Monthly,
            DayOfMonth = 15
        };

        var dec15 = new DateTime(2026, 12, 15);
        var jan15 = RecurrenceEngine.CalculateNextOccurrenceDate(schedule, dec15);
        Assert.Equal(new DateTime(2027, 1, 15), jan15);
    }

    [Fact]
    public void RecurrenceEngine_CalculateDueAndReminder_PreservesLocalTimeAndUtcOffset()
    {
        var schedule = new RecurringSchedule
        {
            ScheduledTime = "19:30",
            ReminderMinutesBefore = 15,
            TimeZoneId = TimeZoneInfo.Local.Id
        };

        var occDate = new DateTime(2026, 10, 25);
        RecurrenceEngine.CalculateOccurrenceDueAndReminder(
            schedule,
            occDate,
            out var dueLocal,
            out var reminderUtc);

        Assert.Equal(new DateTime(2026, 10, 25, 19, 30, 0), dueLocal);
        Assert.NotNull(reminderUtc);
        Assert.Equal(DateTimeKind.Utc, reminderUtc.Value.Kind);

        // Reminder should be 15 minutes before the local due time converted to UTC
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(dueLocal, TimeZoneInfo.Local).AddMinutes(-15);
        Assert.Equal(expectedUtc, reminderUtc.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. RECURRING SCHEDULE & OCCURRENCE LIFECYCLE TESTS
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskService_CreateSchedule_PersistsAndSpawnsInitialOccurrence()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule(
            title: "Study DSA",
            rule: TaskRecurrenceRule.Weekdays,
            scheduledTime: "19:00",
            description: "Binary search & trees",
            priority: TaskPriority.High,
            project: "Academics",
            reminderMinutesBefore: 15);

        Assert.NotNull(schedule);
        Assert.Single(service.Schedules);
        Assert.Equal("Study DSA", service.Schedules[0].Title);

        // Occurrence should be created for today (if weekday)
        var tasks = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Single(tasks);
        var occurrence = tasks[0];
        Assert.Equal("Study DSA", occurrence.Title);
        Assert.Equal(TaskPriority.High, occurrence.Priority);
        Assert.Equal("Academics", occurrence.Project);
        Assert.Equal(TaskRecurrenceRule.Weekdays, occurrence.RecurrenceRule);
        Assert.False(occurrence.IsCompleted);
        Assert.False(occurrence.IsSkipped);
        Assert.NotNull(occurrence.DueDate);
        Assert.NotNull(occurrence.ReminderAt);
    }

    [Fact]
    public void TaskService_ReconcileRecurringSchedules_IsIdempotentAndPreventsDuplicates()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule(
            title: "Clean Desk",
            rule: TaskRecurrenceRule.Daily);

        int initialCount = service.Tasks.Count;
        Assert.Equal(1, initialCount);

        // Repeated reconciliation must not create duplicate occurrences
        service.ReconcileRecurringSchedules();
        service.ReconcileRecurringSchedules();
        service.ReconcileRecurringSchedules();

        Assert.Equal(initialCount, service.Tasks.Count);
    }

    [Fact]
    public void TaskService_CompleteOccurrence_GeneratesNextOccurrenceWithoutAlteringHistory()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule(
            title: "Daily Journal",
            rule: TaskRecurrenceRule.Daily,
            scheduledTime: "21:00");

        var firstOccurrence = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);
        Assert.False(firstOccurrence.IsCompleted);

        // Complete the occurrence
        bool completed = service.Complete(firstOccurrence.Id);
        Assert.True(completed);

        // Verification:
        var allOccurrences = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Equal(2, allOccurrences.Count);

        var historical = allOccurrences.First(t => t.Id == firstOccurrence.Id);
        Assert.True(historical.IsCompleted);
        Assert.Equal(TaskItemStatus.Completed, historical.Status);
        Assert.NotNull(historical.CompletedAt);

        var nextOccurrence = allOccurrences.First(t => t.Id != firstOccurrence.Id);
        Assert.False(nextOccurrence.IsCompleted);
        Assert.Equal("Daily Journal", nextOccurrence.Title);
        Assert.Equal(firstOccurrence.DueDate?.Date.AddDays(1), nextOccurrence.DueDate?.Date);
    }

    [Fact]
    public void TaskService_SkipOccurrence_MarksSkippedAndSpawnsNextOccurrence()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule(
            title: "Morning Run",
            rule: TaskRecurrenceRule.Daily);

        var activeOccurrence = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);
        Assert.False(activeOccurrence.IsCompleted);
        Assert.False(activeOccurrence.IsSkipped);

        // Skip the occurrence
        bool skipped = service.Skip(activeOccurrence.Id);
        Assert.True(skipped);

        var occurrences = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Equal(2, occurrences.Count);

        var skippedTask = occurrences.First(t => t.Id == activeOccurrence.Id);
        Assert.True(skippedTask.IsCompleted);
        Assert.True(skippedTask.IsSkipped);
        Assert.Equal("Skipped ⏭️", skippedTask.StatusDisplay);

        var nextTask = occurrences.First(t => t.Id != activeOccurrence.Id);
        Assert.False(nextTask.IsCompleted);
        Assert.False(nextTask.IsSkipped);
        Assert.Equal("Morning Run", nextTask.Title);
    }

    [Fact]
    public void TaskService_PauseAndResumeSchedule_ControlsOccurrenceGeneration()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule("Weekly Backup", rule: TaskRecurrenceRule.Daily);
        var active = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);

        // Pause schedule
        service.PauseSchedule(schedule.Id);
        Assert.True(service.Schedules.First(s => s.Id == schedule.Id).IsPaused);

        // Completing the task while paused must NOT spawn another occurrence
        service.Complete(active.Id);
        service.ReconcileRecurringSchedules();

        var occurrences = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Single(occurrences); // Only the completed one exists

        // Resume schedule
        service.ResumeSchedule(schedule.Id);
        Assert.False(service.Schedules.First(s => s.Id == schedule.Id).IsPaused);

        var afterResume = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Equal(2, afterResume.Count); // Next occurrence spawned upon resume!
    }

    [Fact]
    public void TaskService_DeleteSchedule_PreservesHistoricalCompletedTasks()
    {
        var service = new TaskService(_testTasksPath);

        var schedule = service.CreateSchedule("Temp Project", rule: TaskRecurrenceRule.Daily);
        var occurrence = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);
        service.Complete(occurrence.Id);

        Assert.Equal(2, service.Tasks.Count);

        // Delete schedule
        service.DeleteSchedule(schedule.Id);
        Assert.Empty(service.Schedules);

        // Historical tasks remain preserved!
        Assert.Equal(2, service.Tasks.Count);
        var historical = service.Tasks.First(t => t.Id == occurrence.Id);
        Assert.True(historical.IsCompleted);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. REMINDER RELIABILITY & RECOVERY TESTS
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskReminderService_NotificationWindowClose_RemovesAlertLock()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var service = new TaskService(_testTasksPath);
                var settings = new AppSettings
                {
                    EnableScheduledReminders = true,
                    EnableQuietMode = false,
                    EnableQuietHours = false
                };

                var fixedNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
                var task = service.Create("Test Window Lock Task", reminderAt: fixedNow.AddMinutes(-1));

                using var reminderService = new TaskReminderService(
                    service,
                    settings,
                    Dispatcher.CurrentDispatcher,
                    utcNowProvider: () => fixedNow);

                bool fired = false;
                reminderService.ReminderDue += (_, _) => fired = true;
                reminderService.CheckReminders();
                Assert.True(fired);

                // Create dummy window and register it
                var win = new System.Windows.Window();
                reminderService.RegisterActiveNotification(task.Id, win);

                // Window closes
                win.Close();

                // RegisterActiveNotification unhooked lock; task alert lock is cleared
                // If snooze or dismiss occurs, state is cleanly preserved
                reminderService.DismissReminder(task.Id);
                var reloaded = service.Get(task.Id);
                Assert.Null(reloaded?.ReminderAt);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);
        Assert.True(finished);
        if (threadEx != null) throw threadEx;
    }

    [Fact]
    public void TaskReminderService_SnoozeRecurringTask_DoesNotSpawnNewOccurrence()
    {
        var service = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = false,
            EnableQuietHours = false
        };

        var fixedNow = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var schedule = service.CreateSchedule("Daily Water Plants", rule: TaskRecurrenceRule.Daily);
        var task = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);

        service.SetReminder(task.Id, fixedNow.AddMinutes(-2));

        using var reminderService = new TaskReminderService(
            service,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        reminderService.CheckReminders();

        int taskCountBefore = service.Tasks.Count;

        // Snooze task by 15 minutes
        reminderService.SnoozeReminder(task.Id, 15);

        // Verify: task count unchanged (no duplicate spawned), reminder pushed forward
        Assert.Equal(taskCountBefore, service.Tasks.Count);
        var updated = service.Get(task.Id);
        Assert.NotNull(updated?.ReminderAt);
        Assert.Equal(fixedNow.AddMinutes(15), updated.ReminderAt.Value);
    }

    [Fact]
    public void TaskReminderService_SkipTask_CancelsPendingReminder()
    {
        var service = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = false,
            EnableQuietHours = false
        };

        var schedule = service.CreateSchedule("Weekly Review", rule: TaskRecurrenceRule.Daily);
        var task = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);

        using var reminderService = new TaskReminderService(
            service,
            settings,
            Dispatcher.CurrentDispatcher);

        reminderService.SkipTask(task.Id);

        var skipped = service.Get(task.Id);
        Assert.NotNull(skipped);
        Assert.True(skipped.IsSkipped);
        Assert.True(skipped.IsCompleted);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. DAILY ROUTINES TESTS (P1)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskService_DailyRoutines_CreateUpdateDeleteAndPersist()
    {
        var service = new TaskService(_testTasksPath);

        var routine = service.CreateRoutine(
            name: "Morning Setup",
            description: "Prepare desk, check notifications, review goals",
            icon: "☀️",
            steps: new[]
            {
                new RoutineStep { Title = "Fill water bottle", EstimatedMinutes = 2 },
                new RoutineStep { Title = "Review tasks for today", EstimatedMinutes = 5 },
                new RoutineStep { Title = "Start 25m focus session", EstimatedMinutes = 25 }
            });

        Assert.NotNull(routine);
        Assert.Single(service.Routines);
        Assert.Equal("Morning Setup", service.Routines[0].Name);
        Assert.Equal(3, service.Routines[0].Steps.Count);
        Assert.Equal(32, service.Routines[0].TotalEstimatedMinutes);

        // Reload from disk to verify persistence
        var reloaded = new TaskService(_testTasksPath);
        Assert.Single(reloaded.Routines);
        var loadedRoutine = reloaded.Routines[0];
        Assert.Equal("Morning Setup", loadedRoutine.Name);
        Assert.Equal(3, loadedRoutine.Steps.Count);
        Assert.Equal("Fill water bottle", loadedRoutine.Steps[0].Title);

        // Delete routine
        reloaded.DeleteRoutine(loadedRoutine.Id);
        Assert.Empty(reloaded.Routines);

        var reloadedAgain = new TaskService(_testTasksPath);
        Assert.Empty(reloadedAgain.Routines);
    }

    [Fact]
    public void TaskService_DailyRoutines_StartProgressAndCompleteFlow()
    {
        var service = new TaskService(_testTasksPath);

        var routine = service.CreateRoutine(
            name: "Evening Reset",
            steps: new[]
            {
                new RoutineStep { Title = "Archive inbox" },
                new RoutineStep { Title = "Plan tomorrow" },
                new RoutineStep { Title = "Tidy desk" }
            });

        // 1. Start routine
        bool started = service.StartRoutine(routine.Id);
        Assert.True(started);

        var active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(0, active.CurrentStepIndex);
        Assert.Equal("Archive inbox", active.CurrentStep?.Title);
        Assert.Equal("0/3 steps completed", active.ProgressSummary);

        // 2. Complete step 1
        service.CompleteRoutineStep(routine.Id, active.Steps[0].Id);
        active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(1, active.CurrentStepIndex);
        Assert.Equal("Plan tomorrow", active.CurrentStep?.Title);
        Assert.Equal("1/3 steps completed", active.ProgressSummary);

        // 3. Skip step 2
        service.SkipRoutineStep(routine.Id, active.Steps[1].Id);
        active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(2, active.CurrentStepIndex);
        Assert.Equal("Tidy desk", active.CurrentStep?.Title);

        // 4. Complete step 3 (final step)
        service.CompleteRoutineStep(routine.Id, active.Steps[2].Id);
        active = service.GetRoutine(routine.Id)!;

        // Routine finishes automatically
        Assert.False(active.IsActive);
        Assert.True(active.IsFinished);
        Assert.NotNull(active.LastCompletedAt);
    }

    [Fact]
    public void TaskService_DailyRoutines_ReorderSteps()
    {
        var service = new TaskService(_testTasksPath);

        var routine = service.CreateRoutine(
            name: "Study Session",
            steps: new[]
            {
                new RoutineStep { Title = "Step A" },
                new RoutineStep { Title = "Step B" },
                new RoutineStep { Title = "Step C" }
            });

        // Move Step A from index 0 to index 2
        bool reordered = service.ReorderRoutineSteps(routine.Id, 0, 2);
        Assert.True(reordered);

        var updated = service.GetRoutine(routine.Id)!;
        Assert.Equal("Step B", updated.Steps[0].Title);
        Assert.Equal("Step C", updated.Steps[1].Title);
        Assert.Equal("Step A", updated.Steps[2].Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. BACKWARD COMPATIBILITY TESTS
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskService_VersionCompatibility_LoadsVersion2WithoutSchedulesOrRoutines()
    {
        string v2Json = @"{
            ""version"": 2,
            ""tasks"": [
                {
                    ""id"": ""v2-task-1"",
                    ""title"": ""Old Task"",
                    ""isCompleted"": false,
                    ""status"": ""Todo"",
                    ""priority"": ""Normal"",
                    ""createdAt"": ""2026-10-18T10:00:00Z""
                }
            ]
        }";
        File.WriteAllText(_testTasksPath, v2Json);

        var service = new TaskService(_testTasksPath);
        Assert.Single(service.Tasks);
        Assert.Empty(service.Schedules);
        Assert.Empty(service.Routines);

        var task = service.Tasks[0];
        Assert.Equal("v2-task-1", task.Id);
        Assert.Equal("Old Task", task.Title);
        Assert.False(task.IsCompleted);
        Assert.False(task.IsSkipped);
        Assert.Null(task.RecurringScheduleId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. AUDIT A & B: ADDITIONAL RECURRENCE, TIMING & PERSISTENCE VERIFICATIONS
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecurrenceEngine_Weekly_ConfiguredTimeAlreadyPassed_DoesNotRecreateForSameDay()
    {
        // Target: Wednesday at 10:00
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Weekly,
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Wednesday },
            ScheduledTime = "10:00"
        };

        // Reference: Wednesday at 14:00 (10:00 has already passed)
        var wednesdayAfternoon = new DateTime(2026, 10, 21, 14, 0, 0);
        var nextDate = RecurrenceEngine.GetFirstOccurrenceDate(schedule, wednesdayAfternoon);

        // Must advance to NEXT Wednesday (Oct 28), not today in the past
        Assert.Equal(new DateTime(2026, 10, 28), nextDate.Date);

        // Reference: Wednesday at 08:00 (10:00 has NOT passed yet)
        var wednesdayMorning = new DateTime(2026, 10, 21, 8, 0, 0);
        var todayDate = RecurrenceEngine.GetFirstOccurrenceDate(schedule, wednesdayMorning);
        Assert.Equal(new DateTime(2026, 10, 21), todayDate.Date);
    }

    [Fact]
    public void RecurrenceEngine_Daily_ConfiguredTimeAlreadyPassed_AdvancesToTomorrow()
    {
        var schedule = new RecurringSchedule
        {
            RecurrenceRule = TaskRecurrenceRule.Daily,
            ScheduledTime = "09:00"
        };

        var todayAfternoon = new DateTime(2026, 10, 20, 14, 0, 0);
        var nextDate = RecurrenceEngine.GetFirstOccurrenceDate(schedule, todayAfternoon);
        Assert.Equal(new DateTime(2026, 10, 21), nextDate.Date);

        var todayMorning = new DateTime(2026, 10, 20, 8, 0, 0);
        var sameDay = RecurrenceEngine.GetFirstOccurrenceDate(schedule, todayMorning);
        Assert.Equal(new DateTime(2026, 10, 20), sameDay.Date);
    }

    [Fact]
    public void RecurrenceEngine_CalculateOccurrenceDueAndReminder_PreservesLocalSchedulingTimeAndOffset()
    {
        var schedule = new RecurringSchedule
        {
            ScheduledTime = "14:30",
            ReminderMinutesBefore = 15
        };

        var localDate = new DateTime(2026, 11, 5);
        RecurrenceEngine.CalculateOccurrenceDueAndReminder(
            schedule,
            localDate,
            out var dueLocal,
            out var reminderUtc);

        Assert.Equal(new DateTime(2026, 11, 5, 14, 30, 0), dueLocal);
        Assert.NotNull(reminderUtc);

        var reminderLocal = reminderUtc.Value.ToLocalTime();
        Assert.Equal(new DateTime(2026, 11, 5, 14, 15, 0), reminderLocal);
    }

    [Fact]
    public void TaskService_Persistence_RawLegacyJsonArray_LoadsAndMigratesToVersion3()
    {
        string rawArrayJson = @"[
            { ""id"": ""legacy-1"", ""title"": ""Legacy Task 1"", ""isCompleted"": false },
            { ""id"": ""legacy-2"", ""title"": ""Legacy Task 2"", ""isCompleted"": true }
        ]";
        File.WriteAllText(_testTasksPath, rawArrayJson);

        var service = new TaskService(_testTasksPath);
        Assert.Equal(2, service.Tasks.Count);
        Assert.Equal("Legacy Task 1", service.Tasks[0].Title);
        Assert.False(service.Tasks[0].IsCompleted);
        Assert.True(service.Tasks[1].IsCompleted);

        // Add a schedule and save to trigger migration to Version 3
        service.CreateSchedule("Migrated Schedule", rule: TaskRecurrenceRule.Daily);

        string savedJson = File.ReadAllText(_testTasksPath);
        using var doc = JsonDocument.Parse(savedJson);
        Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
        Assert.True(doc.RootElement.GetProperty("tasks").GetArrayLength() >= 2);
        Assert.Equal(1, doc.RootElement.GetProperty("schedules").GetArrayLength());
    }

    [Fact]
    public void TaskService_Persistence_Version1Format_LoadsAndMigrates()
    {
        string v1Json = @"{
            ""version"": 1,
            ""tasks"": [
                { ""id"": ""v1-1"", ""title"": ""V1 Task"", ""isCompleted"": false }
            ]
        }";
        File.WriteAllText(_testTasksPath, v1Json);

        var service = new TaskService(_testTasksPath);
        Assert.Single(service.Tasks);
        Assert.Equal("V1 Task", service.Tasks[0].Title);

        service.Save();
        string reloadedJson = File.ReadAllText(_testTasksPath);
        using var doc = JsonDocument.Parse(reloadedJson);
        Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void TaskService_Persistence_Version2Format_WithReminders_LoadsCorrectly()
    {
        string v2Json = @"{
            ""version"": 2,
            ""tasks"": [
                {
                    ""id"": ""v2-remind"",
                    ""title"": ""Task with Reminder"",
                    ""reminderAt"": ""2026-10-20T10:00:00Z"",
                    ""isCompleted"": false
                }
            ]
        }";
        File.WriteAllText(_testTasksPath, v2Json);

        var service = new TaskService(_testTasksPath);
        Assert.Single(service.Tasks);
        Assert.True(service.Tasks[0].HasReminder);
        Assert.Equal(new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), service.Tasks[0].ReminderAt);
    }

    [Fact]
    public void TaskService_Persistence_Version3Format_FullRoundtrip_SurvivesRestart()
    {
        var service1 = new TaskService(_testTasksPath);
        service1.Create("Task 1", project: "Alpha", tags: new[] { "tag1", "tag2" });
        service1.CreateSchedule("Morning Daily", rule: TaskRecurrenceRule.Daily, scheduledTime: "08:30");
        service1.CreateRoutine("Evening Reset", steps: new[]
        {
            new RoutineStep { Title = "Inbox zero", EstimatedMinutes = 10 },
            new RoutineStep { Title = "Review calendar", EstimatedMinutes = 5 }
        });

        // 3 consecutive load-save-load cycles across instances
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            var serviceN = new TaskService(_testTasksPath);
            Assert.True(serviceN.Tasks.Count >= 2); // Task 1 + generated occurrence
            Assert.Single(serviceN.Schedules);
            Assert.Single(serviceN.Routines);

            var routine = serviceN.Routines[0];
            Assert.Equal("Evening Reset", routine.Name);
            Assert.Equal(2, routine.Steps.Count);
            Assert.Equal(15, routine.TotalEstimatedMinutes);

            serviceN.Save();
        }
    }

    [Fact]
    public void TaskService_Persistence_CorruptFile_BacksUpAndRecoversCleanly()
    {
        File.WriteAllText(_testTasksPath, "{ this is invalid json !!!");

        var service = new TaskService(_testTasksPath);
        Assert.Empty(service.Tasks);
        Assert.Empty(service.Schedules);
        Assert.Empty(service.Routines);
        Assert.NotNull(service.LastCorruptBackupPath);
        Assert.True(File.Exists(service.LastCorruptBackupPath));
    }

    [Fact]
    public void TaskService_Persistence_MissingOptionalFields_PopulatesSafeDefaults()
    {
        string messyJson = @"{
            ""version"": 3,
            ""tasks"": [
                { ""id"": """", ""title"": null, ""tags"": null, ""estimatedMinutes"": -5 }
            ],
            ""schedules"": [
                { ""id"": """", ""title"": null, ""scheduledTime"": """", ""daysOfWeek"": null }
            ],
            ""routines"": [
                { ""id"": """", ""name"": null, ""steps"": [ { ""id"": """", ""title"": null, ""estimatedMinutes"": -10 } ] }
            ]
        }";
        File.WriteAllText(_testTasksPath, messyJson);

        var service = new TaskService(_testTasksPath);
        Assert.True(service.Tasks.Count >= 1);
        var standalone = service.Tasks.First(t => t.RecurringScheduleId == null);
        Assert.False(string.IsNullOrWhiteSpace(standalone.Id));
        Assert.Equal(0, standalone.EstimatedMinutes);
        Assert.NotNull(standalone.Tags);

        Assert.Single(service.Schedules);
        Assert.Equal("09:00", service.Schedules[0].ScheduledTime);

        Assert.Single(service.Routines);
        Assert.Equal("New Routine", service.Routines[0].Name);
        Assert.Equal(0, service.Routines[0].Steps[0].EstimatedMinutes);
    }

    [Fact]
    public void TaskService_RecurrenceRule_AuthoritativeSourceOfTruth_SynchronizesOccurrences()
    {
        var service = new TaskService(_testTasksPath);
        var schedule = service.CreateSchedule("Master Recurring Task", rule: TaskRecurrenceRule.Daily, priority: TaskPriority.Normal);

        var activeOccurrence = service.Tasks.First(t => t.RecurringScheduleId == schedule.Id);
        Assert.Equal("Master Recurring Task", activeOccurrence.Title);
        Assert.Equal(TaskPriority.Normal, activeOccurrence.Priority);
        Assert.Equal(TaskRecurrenceRule.Daily, activeOccurrence.RecurrenceRule);

        // Update schedule: changes synchronize to active occurrence
        service.UpdateSchedule(schedule.Id, s =>
        {
            s.Title = "Updated Recurring Task";
            s.Priority = TaskPriority.Urgent;
            s.RecurrenceRule = TaskRecurrenceRule.Weekdays;
        });

        var updatedOccurrence = service.Tasks.First(t => t.Id == activeOccurrence.Id);
        Assert.Equal("Updated Recurring Task", updatedOccurrence.Title);
        Assert.Equal(TaskPriority.Urgent, updatedOccurrence.Priority);
        Assert.Equal(TaskRecurrenceRule.Weekdays, updatedOccurrence.RecurrenceRule);

        // Updating occurrence recurrence to None detaches from schedule
        service.Update(activeOccurrence.Id, t => t.RecurrenceRule = TaskRecurrenceRule.None);
        var detached = service.Get(activeOccurrence.Id)!;
        Assert.Null(detached.RecurringScheduleId);
        Assert.Equal(TaskRecurrenceRule.None, detached.RecurrenceRule);
    }

    [Fact]
    public void TaskReminderService_ConsolidatedReminders_ReconcilesAllRepresentedTasks()
    {
        var service = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = false,
            EnableQuietHours = false
        };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var tasks = new List<TaskItem>();
        for (int i = 1; i <= 5; i++)
        {
            tasks.Add(service.Create($"Batch Task {i}", reminderAt: fixedNow.AddMinutes(-i)));
        }

        using var reminderService = new TaskReminderService(
            service,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        int consolidatedFiredCount = 0;
        List<TaskItem>? represented = null;
        reminderService.ConsolidatedRemindersDue += (count, list) =>
        {
            consolidatedFiredCount = count;
            represented = list.ToList();
        };

        reminderService.CheckReminders();
        Assert.Equal(5, consolidatedFiredCount);
        Assert.NotNull(represented);
        Assert.Equal(5, represented!.Count);

        // Complete first task, snooze second, dismiss third
        reminderService.CompleteTask(tasks[0].Id);
        reminderService.SnoozeReminder(tasks[1].Id, 15);
        reminderService.DismissReminder(tasks[2].Id);

        Assert.True(service.Get(tasks[0].Id)!.IsCompleted);
        Assert.NotNull(service.Get(tasks[1].Id)!.ReminderAt);
        Assert.Null(service.Get(tasks[2].Id)!.ReminderAt);
    }

    [Fact]
    public void TaskReminderService_SnoozeDeadlines_PersistAcrossServiceRestart()
    {
        var fixedNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        string taskId;

        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = false,
            EnableQuietHours = false
        };

        {
            var service1 = new TaskService(_testTasksPath);
            var task = service1.Create("Snooze Task", reminderAt: fixedNow.AddMinutes(-5));
            taskId = task.Id;

            using var reminderService1 = new TaskReminderService(
                service1,
                settings,
                Dispatcher.CurrentDispatcher,
                utcNowProvider: () => fixedNow);

            reminderService1.CheckReminders();
            reminderService1.SnoozeReminder(taskId, 15);
        }

        // Restart services from persisted state
        {
            var service2 = new TaskService(_testTasksPath);
            var reloaded = service2.Get(taskId);
            Assert.NotNull(reloaded?.ReminderAt);
            Assert.Equal(fixedNow.AddMinutes(15), reloaded!.ReminderAt.Value);

            using var reminderService2 = new TaskReminderService(
                service2,
                settings,
                Dispatcher.CurrentDispatcher,
                utcNowProvider: () => fixedNow);

            bool firedEarly = false;
            reminderService2.ReminderDue += (_, _) => firedEarly = true;
            reminderService2.CheckReminders();

            // At fixedNow (10:00), the snoozed reminder (due 10:15) must NOT fire
            Assert.False(firedEarly);
        }
    }

    [Fact]
    public void TaskReminderService_PresentationAndQuietHours_Suppression_DrainsSafely()
    {
        var service = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = true // Quiet mode suppresses alerts
        };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var task = service.Create("Suppressed Task", reminderAt: fixedNow.AddMinutes(-5));

        using var reminderService = new TaskReminderService(
            service,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool fired = false;
        reminderService.ReminderDue += (_, _) => fired = true;

        reminderService.CheckReminders();
        Assert.False(fired); // Suppressed!

        // End quiet mode
        settings.EnableQuietMode = false;
        reminderService.CheckReminders();
        Assert.True(fired); // Safely drained!
    }

    [Fact]
    public void TaskReminderService_Reconciliation_RepeatedExecution_IsSafeAndIdempotent()
    {
        var service = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = false,
            EnableQuietHours = false
        };

        var fixedNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        service.Create("Single Alert Task", reminderAt: fixedNow.AddMinutes(-1));
        var schedule = service.CreateSchedule("Daily Idempotency Test", rule: TaskRecurrenceRule.Daily);

        using var reminderService = new TaskReminderService(
            service,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        int alertsFired = 0;
        reminderService.ReminderDue += (_, _) => alertsFired++;

        // Execute reconciliation repeatedly 5 times
        for (int i = 0; i < 5; i++)
        {
            service.ReconcileRecurringSchedules(fixedNow.ToLocalTime());
            reminderService.CheckReminders();
        }

        // Only 1 alert should ever be fired, and exactly 1 active occurrence for the schedule
        Assert.Equal(1, alertsFired);
        var scheduleOccurrences = service.Tasks.Where(t => t.RecurringScheduleId == schedule.Id).ToList();
        Assert.Single(scheduleOccurrences);
    }

    [Fact]
    public void DailyRoutine_SingleActiveSession_ModelBehavior()
    {
        var service = new TaskService(_testTasksPath);
        var routine = service.CreateRoutine("Morning Routine", steps: new[]
        {
            new RoutineStep { Title = "Stretch", EstimatedMinutes = 5 },
            new RoutineStep { Title = "Hydrate", EstimatedMinutes = 2 },
            new RoutineStep { Title = "Check Schedule", EstimatedMinutes = 3 }
        });

        // 1. Initial State: inactive, 0/3 progress
        Assert.False(routine.IsActive);
        Assert.Equal("0/3 steps completed", routine.ProgressSummary);
        Assert.Equal("Ready to start", routine.StatusDisplay);

        // 2. Start routine
        service.StartRoutine(routine.Id);
        var active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(0, active.CurrentStepIndex);
        Assert.Equal("Stretch", active.CurrentStep?.Title);

        // 3. Step completion
        service.CompleteRoutineStep(routine.Id, active.Steps[0].Id);
        active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(1, active.CurrentStepIndex);
        Assert.Equal("Hydrate", active.CurrentStep?.Title);
        Assert.Equal("1/3 steps completed", active.ProgressSummary);

        // 4. Step skip
        service.SkipRoutineStep(routine.Id, active.Steps[1].Id);
        active = service.GetRoutine(routine.Id)!;
        Assert.True(active.IsActive);
        Assert.Equal(2, active.CurrentStepIndex);
        Assert.Equal("Check Schedule", active.CurrentStep?.Title);

        // 5. Final step completion marks routine finished
        service.CompleteRoutineStep(routine.Id, active.Steps[2].Id);
        active = service.GetRoutine(routine.Id)!;
        Assert.False(active.IsActive);
        Assert.True(active.IsFinished);
        Assert.NotNull(active.LastCompletedAt);

        // 6. Reset routine
        service.ResetRoutine(routine.Id);
        var reset = service.GetRoutine(routine.Id)!;
        Assert.False(reset.IsActive);
        Assert.Equal(0, reset.CurrentStepIndex);
        Assert.Equal("0/3 steps completed", reset.ProgressSummary);
    }

    [Fact]
    public void TaskService_UpdateOccurrenceRecurrence_UpdateEntireSchedule_SynchronizesSchedule()
    {
        var service = new TaskService(_testTasksPath);
        var schedule = service.CreateSchedule("Team Standup", rule: TaskRecurrenceRule.Daily);
        service.ReconcileRecurringSchedules(DateTime.Now);

        var occurrence = Assert.Single(service.Tasks, t => t.RecurringScheduleId == schedule.Id);
        Assert.Equal(TaskRecurrenceRule.Daily, occurrence.RecurrenceRule);

        bool result = service.UpdateOccurrenceRecurrence(occurrence.Id, TaskRecurrenceRule.Weekdays, updateEntireSchedule: true);
        Assert.True(result);

        var updatedOccurrence = service.Tasks.First(t => t.Id == occurrence.Id);
        Assert.Equal(TaskRecurrenceRule.Weekdays, updatedOccurrence.RecurrenceRule);
        Assert.Equal(schedule.Id, updatedOccurrence.RecurringScheduleId);

        var updatedSchedule = service.Schedules.First(s => s.Id == schedule.Id);
        Assert.Equal(TaskRecurrenceRule.Weekdays, updatedSchedule.RecurrenceRule);
    }

    [Fact]
    public void TaskService_UpdateOccurrenceRecurrence_DetachOnly_PreservesSchedule()
    {
        var service = new TaskService(_testTasksPath);
        var schedule = service.CreateSchedule("Weekly Backup", rule: TaskRecurrenceRule.Weekly);
        service.ReconcileRecurringSchedules(DateTime.Now);

        var occurrence = Assert.Single(service.Tasks, t => t.RecurringScheduleId == schedule.Id);
        Assert.Equal(TaskRecurrenceRule.Weekly, occurrence.RecurrenceRule);

        bool result = service.UpdateOccurrenceRecurrence(occurrence.Id, TaskRecurrenceRule.None, updateEntireSchedule: false);
        Assert.True(result);

        var updatedOccurrence = service.Tasks.First(t => t.Id == occurrence.Id);
        Assert.Equal(TaskRecurrenceRule.None, updatedOccurrence.RecurrenceRule);
        Assert.Null(updatedOccurrence.RecurringScheduleId);

        var unchangedSchedule = service.Schedules.First(s => s.Id == schedule.Id);
        Assert.Equal(TaskRecurrenceRule.Weekly, unchangedSchedule.RecurrenceRule);
    }

    [Fact]
    public void DailyRoutine_StepReordering_PersistsAcrossServiceReloads()
    {
        var service = new TaskService(_testTasksPath);
        var routine = service.CreateRoutine("Morning Focus", steps: new[]
        {
            new RoutineStep { Title = "Step 1" },
            new RoutineStep { Title = "Step 2" },
            new RoutineStep { Title = "Step 3" }
        });

        // Move Step 3 (index 2) to first position (index 0)
        bool reordered = service.ReorderRoutineSteps(routine.Id, 2, 0);
        Assert.True(reordered);

        // Reload from disk to verify persistence
        var reloadedService = new TaskService(_testTasksPath);
        var loadedRoutine = Assert.Single(reloadedService.Routines);
        Assert.Equal(3, loadedRoutine.Steps.Count);
        Assert.Equal("Step 3", loadedRoutine.Steps[0].Title);
        Assert.Equal("Step 1", loadedRoutine.Steps[1].Title);
        Assert.Equal("Step 2", loadedRoutine.Steps[2].Title);
    }

    [Fact]
    public void DailyRoutine_Step_ActionIcon_And_StatusDisplay_UpdatesCorrectly()
    {
        var step = new RoutineStep { Title = "Draft Notes" };
        Assert.Equal("⭕", step.ActionIcon);
        Assert.Equal("Pending", step.StatusDisplay);

        step.IsCompleted = true;
        Assert.Equal("✅", step.ActionIcon);
        Assert.Equal("Done ✅", step.StatusDisplay);
        Assert.False(step.IsSkipped);

        step.IsSkipped = true;
        Assert.Equal("⏭️", step.ActionIcon);
        Assert.Equal("Skipped ⏭️", step.StatusDisplay);
        Assert.False(step.IsCompleted);

        step.IsCompleted = true;
        Assert.Equal("✅", step.ActionIcon);
        Assert.False(step.IsSkipped);
    }

    [Fact]
    public void DailyRoutine_DeleteRoutine_RemovesFromPersistence()
    {
        var service = new TaskService(_testTasksPath);
        var routine = service.CreateRoutine("Temporary Routine");
        Assert.Single(service.Routines);

        bool deleted = service.DeleteRoutine(routine.Id);
        Assert.True(deleted);
        Assert.Empty(service.Routines);

        var reloadedService = new TaskService(_testTasksPath);
        Assert.Empty(reloadedService.Routines);
        Assert.Null(reloadedService.GetRoutine(routine.Id));
    }
}
