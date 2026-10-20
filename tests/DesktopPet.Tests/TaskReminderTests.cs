using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class TaskReminderTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testTasksPath;

    public TaskReminderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NekuReminderTests_" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup
        }
    }

    [Fact]
    public void TaskItem_ReminderDefaults_AreNullOrEmpty()
    {
        var task = new TaskItem();

        Assert.Null(task.ReminderAt);
        Assert.False(task.HasReminder);
        Assert.False(task.IsReminderOverdue);
        Assert.Equal(string.Empty, task.FormattedReminder);
    }

    [Fact]
    public void TaskItem_HasReminder_ReturnsTrueWhenSet()
    {
        var task = new TaskItem
        {
            ReminderAt = DateTime.UtcNow.AddMinutes(30)
        };

        Assert.True(task.HasReminder);
        Assert.False(task.IsReminderOverdue);
        Assert.False(string.IsNullOrEmpty(task.FormattedReminder));
    }

    [Fact]
    public void TaskItem_IsReminderOverdue_CalculatesCorrectly()
    {
        var pastTask = new TaskItem
        {
            ReminderAt = DateTime.UtcNow.AddMinutes(-10),
            IsCompleted = false
        };
        Assert.True(pastTask.IsReminderOverdue);

        // Completed task should not be considered reminder overdue
        pastTask.IsCompleted = true;
        Assert.False(pastTask.IsReminderOverdue);

        var futureTask = new TaskItem
        {
            ReminderAt = DateTime.UtcNow.AddMinutes(10),
            IsCompleted = false
        };
        Assert.False(futureTask.IsReminderOverdue);
    }

    [Fact]
    public void TaskService_Create_WithReminderAt_NormalizesUtcAndPersists()
    {
        var taskService = new TaskService(_testTasksPath);
        var localReminder = DateTime.Now.AddHours(2);

        var task = taskService.Create(
            title: "Test Reminder Task",
            reminderAt: localReminder);

        Assert.NotNull(task.ReminderAt);
        Assert.Equal(DateTimeKind.Utc, task.ReminderAt.Value.Kind);
        Assert.True(task.HasReminder);

        // Reload from disk to verify persistence
        var reloadedService = new TaskService(_testTasksPath);
        var loadedTask = reloadedService.Tasks.FirstOrDefault(t => t.Id == task.Id);
        Assert.NotNull(loadedTask);
        Assert.NotNull(loadedTask.ReminderAt);
        Assert.Equal(DateTimeKind.Utc, loadedTask.ReminderAt.Value.Kind);
        Assert.Equal(task.ReminderAt.Value, loadedTask.ReminderAt.Value);
    }

    [Fact]
    public void TaskService_SetReminder_UpdatesAndPersistsUtc()
    {
        var taskService = new TaskService(_testTasksPath);
        var task = taskService.Create("No reminder initially");
        Assert.Null(task.ReminderAt);

        var futureUtc = DateTime.UtcNow.AddMinutes(45);
        bool updated = taskService.SetReminder(task.Id, futureUtc);
        Assert.True(updated);

        var reloaded = new TaskService(_testTasksPath);
        var fetched = reloaded.Tasks.First(t => t.Id == task.Id);
        Assert.NotNull(fetched.ReminderAt);
        Assert.Equal(futureUtc, fetched.ReminderAt.Value);

        // Clear reminder
        bool cleared = taskService.SetReminder(task.Id, null);
        Assert.True(cleared);

        var reloaded2 = new TaskService(_testTasksPath);
        var fetched2 = reloaded2.Tasks.First(t => t.Id == task.Id);
        Assert.Null(fetched2.ReminderAt);
    }

    [Fact]
    public void TaskService_BackwardsCompatibility_LoadsOldJsonWithoutReminderAt()
    {
        // Simulate v1.0.2 tasks.json where ReminderAt was not present
        string oldJson = @"[
            {
                ""id"": ""legacy-task-1"",
                ""title"": ""Legacy Task"",
                ""description"": ""From v1.0.2"",
                ""isCompleted"": false,
                ""priority"": 1,
                ""createdAt"": ""2026-01-01T00:00:00Z""
            }
        ]";
        File.WriteAllText(_testTasksPath, oldJson);

        var taskService = new TaskService(_testTasksPath);
        Assert.Single(taskService.Tasks);
        var task = taskService.Tasks[0];
        Assert.Equal("legacy-task-1", task.Id);
        Assert.Null(task.ReminderAt);
        Assert.False(task.HasReminder);
    }

    [Fact]
    public void TaskReminderService_CheckReminders_FiresReminderDue_WhenDue()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, EnableQuietHours = false, EnableQuietMode = false };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var dueTime = fixedNow.AddMinutes(-2);

        var task = taskService.Create("Reminder Due Task", reminderAt: dueTime);

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        TaskItem? alertedTask = null;
        bool wasMissed = false;
        reminderService.ReminderDue += (t, missed) =>
        {
            alertedTask = t;
            wasMissed = missed;
        };

        reminderService.CheckReminders();

        Assert.NotNull(alertedTask);
        Assert.Equal(task.Id, alertedTask.Id);
        Assert.False(wasMissed); // 2 minutes is <= 5 min threshold

        // Duplicate check: Calling CheckReminders again must not re-raise event for active alert
        TaskItem? duplicateAlert = null;
        reminderService.ReminderDue += (t, _) => duplicateAlert = t;
        reminderService.CheckReminders();
        Assert.Null(duplicateAlert);
    }

    [Fact]
    public void TaskReminderService_CheckReminders_MarksMissed_WhenExceeds5Minutes()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, EnableQuietHours = false, EnableQuietMode = false };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var dueTime = fixedNow.AddMinutes(-10); // 10 minutes overdue

        var task = taskService.Create("Missed Task", reminderAt: dueTime);

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool wasMissed = false;
        reminderService.ReminderDue += (_, missed) => wasMissed = missed;

        reminderService.CheckReminders();

        Assert.True(wasMissed);
    }

    [Fact]
    public void TaskReminderService_CheckReminders_DoesNotFire_WhenFuture()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, EnableQuietHours = false, EnableQuietMode = false };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var futureTime = fixedNow.AddMinutes(15);

        taskService.Create("Future Task", reminderAt: futureTime);

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool fired = false;
        reminderService.ReminderDue += (_, _) => fired = true;

        reminderService.CheckReminders();

        Assert.False(fired);
    }

    [Fact]
    public void TaskReminderService_StaleReminderPolicy_ClearsReminderIfOlderThan24Hours()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, EnableQuietHours = false, EnableQuietMode = false };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var ancientTime = fixedNow.AddHours(-25); // Older than 24h

        var task = taskService.Create("Ancient Task", reminderAt: ancientTime);

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool fired = false;
        reminderService.ReminderDue += (_, _) => fired = true;

        reminderService.CheckReminders();

        // Stale reminders should NOT trigger an intrusive alert
        Assert.False(fired);

        // Reminder timestamp must be cleared automatically from TaskService
        var reloadedTask = taskService.Tasks.First(t => t.Id == task.Id);
        Assert.Null(reloadedTask.ReminderAt);
    }

    [Fact]
    public void TaskReminderService_FloodPolicy_FiresConsolidated_WhenMoreThan3RemindersDue()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, EnableQuietHours = false, EnableQuietMode = false };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        for (int i = 1; i <= 5; i++)
        {
            taskService.Create($"Task {i}", reminderAt: fixedNow.AddMinutes(-i));
        }

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        int individualCount = 0;
        int consolidatedCount = 0;
        reminderService.ReminderDue += (_, _) => individualCount++;
        reminderService.ConsolidatedRemindersDue += (count, _) => consolidatedCount = count;

        reminderService.CheckReminders();

        // Since 5 tasks are due (> 3), only ConsolidatedRemindersDue should fire
        Assert.Equal(0, individualCount);
        Assert.Equal(5, consolidatedCount);
    }

    [Fact]
    public void TaskReminderService_QuietHours_SuppressesReminders_AndDrainsAfterQuietHours()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietHours = true,
            EnableQuietMode = false,
            QuietHoursStart = "22:00",
            QuietHoursEnd = "08:00"
        };

        // Pick a fixed local time during quiet hours (e.g., 23:30 local)
        var localNight = new DateTime(2026, 10, 20, 23, 30, 0, DateTimeKind.Local);
        var utcNight = localNight.ToUniversalTime();

        var task = taskService.Create("Late Task", reminderAt: utcNight.AddMinutes(-10));

        var simulatedUtc = utcNight;
        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => simulatedUtc);

        bool firedDuringQuiet = false;
        reminderService.ReminderDue += (_, _) => firedDuringQuiet = true;

        reminderService.CheckReminders();
        Assert.False(firedDuringQuiet, "Reminder should be suppressed during quiet hours");

        // Now advance time to the morning after quiet hours (e.g., 08:30 local)
        var localMorning = new DateTime(2026, 10, 21, 8, 30, 0, DateTimeKind.Local);
        simulatedUtc = localMorning.ToUniversalTime();

        bool firedAfterQuiet = false;
        reminderService.ReminderDue += (_, _) => firedAfterQuiet = true;

        reminderService.CheckReminders();
        Assert.True(firedAfterQuiet, "Reminder should drain and fire after quiet hours end");
    }

    [Fact]
    public void TaskReminderService_QuietMode_SuppressesReminders()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings
        {
            EnableScheduledReminders = true,
            EnableQuietMode = true // Quiet mode turned on
        };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        taskService.Create("Quiet Task", reminderAt: fixedNow.AddMinutes(-5));

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool fired = false;
        reminderService.ReminderDue += (_, _) => fired = true;

        reminderService.CheckReminders();
        Assert.False(fired, "Quiet mode must suppress reminders");
    }

    [Fact]
    public void TaskReminderService_Snooze_UpdatesReminderAt_ByGivenMinutes()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true, DefaultSnoozeMinutes = 15 };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var task = taskService.Create("Snooze Task", reminderAt: fixedNow.AddMinutes(-1));

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        reminderService.CheckReminders();

        bool snoozed = reminderService.SnoozeReminder(task.Id, 15);
        Assert.True(snoozed);

        var updatedTask = taskService.Tasks.First(t => t.Id == task.Id);
        Assert.Equal(fixedNow.AddMinutes(15), updatedTask.ReminderAt);
    }

    [Fact]
    public void TaskReminderService_Dismiss_ClearsReminder()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var task = taskService.Create("Dismiss Task", reminderAt: fixedNow.AddMinutes(-1));

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool dismissed = reminderService.DismissReminder(task.Id, persistTask: true);
        Assert.True(dismissed);

        var updatedTask = taskService.Tasks.First(t => t.Id == task.Id);
        Assert.Null(updatedTask.ReminderAt);
    }

    [Fact]
    public void TaskReminderService_CompleteTask_CompletesTaskInTaskService()
    {
        var taskService = new TaskService(_testTasksPath);
        var settings = new AppSettings { EnableScheduledReminders = true };

        var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
        var task = taskService.Create("Task To Complete", reminderAt: fixedNow.AddMinutes(-1));
        Assert.False(task.IsCompleted);

        using var reminderService = new TaskReminderService(
            taskService,
            settings,
            Dispatcher.CurrentDispatcher,
            utcNowProvider: () => fixedNow);

        bool completed = reminderService.CompleteTask(task.Id);
        Assert.True(completed);

        var updatedTask = taskService.Tasks.First(t => t.Id == task.Id);
        Assert.True(updatedTask.IsCompleted);
    }

    [Fact]
    public void AppSettings_ReminderAndQuickCaptureDefaults_AreValid()
    {
        var settings = new AppSettings();

        Assert.True(settings.EnableScheduledReminders);
        Assert.Equal(15, settings.DefaultSnoozeMinutes);
        Assert.False(settings.EnableQuietHours);
        Assert.Equal("22:00", settings.QuietHoursStart);
        Assert.Equal("08:00", settings.QuietHoursEnd);

        Assert.True(settings.EnableQuickCaptureHotkey);
        Assert.Equal(2 | 1, settings.QuickCaptureHotkeyModifiers); // Ctrl + Alt
        Assert.Equal(46, settings.QuickCaptureHotkeyKey);          // 'C'

        // Validation test
        settings.DefaultSnoozeMinutes = -5;
        settings.QuietHoursStart = "";
        settings.QuietHoursEnd = "";
        settings.QuickCaptureHotkeyModifiers = 0;
        settings.QuickCaptureHotkeyKey = 0;

        settings.Validate();

        Assert.Equal(15, settings.DefaultSnoozeMinutes);
        Assert.Equal("22:00", settings.QuietHoursStart);
        Assert.Equal("08:00", settings.QuietHoursEnd);
        Assert.Equal(2 | 1, settings.QuickCaptureHotkeyModifiers);
        Assert.Equal(46, settings.QuickCaptureHotkeyKey);
    }

    [Fact]
    public void ReminderNotificationWindow_NoFocusSteal_WindowPropertiesVerified()
    {
        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var settings = new AppSettings();
                var reminderService = new TaskReminderService(taskService, settings, Dispatcher.CurrentDispatcher);
                var task = taskService.Create("No Focus Steal Test");

                var win = new DesktopPet.UI.ReminderNotificationWindow(task, reminderService);

                Assert.False(win.ShowActivated, "Window must not activate on Show");
                Assert.False(win.Focusable, "Window must not participate in standard focus navigation");
                Assert.True(win.Topmost, "Window should be topmost for visibility");
                Assert.False(win.ShowInTaskbar, "Window should not clutter taskbar");

                win.Close();
                reminderService.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "ReminderNotificationWindow focus properties test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void ReminderNotificationWindow_ConsolidatedNotification_DisplaysAccurateInformationAndActions()
    {
        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var settings = new AppSettings();
                var fixedNow = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
                var reminderService = new TaskReminderService(
                    taskService,
                    settings,
                    Dispatcher.CurrentDispatcher,
                    utcNowProvider: () => fixedNow);

                var tasks = new List<TaskItem>
                {
                    taskService.Create("First Priority Task", reminderAt: fixedNow.AddMinutes(-10)),
                    taskService.Create("Second Urgent Task", reminderAt: fixedNow.AddMinutes(-8)),
                    taskService.Create("Third Followup Task", reminderAt: fixedNow.AddMinutes(-5)),
                    taskService.Create("Fourth Review Task", reminderAt: fixedNow.AddMinutes(-2))
                };

                bool viewTasksClicked = false;
                var win = new DesktopPet.UI.ReminderNotificationWindow(
                    tasks,
                    reminderService,
                    onOpenTasks: () => viewTasksClicked = true);

                // Verify accurate summary info
                Assert.Equal("4 Missed Reminders", win.TitleText.Text);
                Assert.Contains("First Priority Task", win.SubText.Text);
                Assert.Contains("Second Urgent Task", win.SubText.Text);
                Assert.Contains("and 2 more", win.SubText.Text);
                Assert.Equal("Review Tasks 📋", win.CompleteButton.Content);
                Assert.Equal("Review Tasks", System.Windows.Automation.AutomationProperties.GetName(win.CompleteButton));
                Assert.Equal("💤 Snooze All (15m)", win.SnoozeButton.Content);
                Assert.Equal(System.Windows.Visibility.Visible, win.SnoozeButton.Visibility);

                // Test Review Tasks action: opens Tasks window WITHOUT completing represented tasks
                win.CompleteButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(viewTasksClicked, "Clicking CompleteButton on consolidated notification must invoke onOpenTasks");

                // Confirm that none of the represented tasks were marked completed
                foreach (var t in tasks)
                {
                    var taskState = taskService.Tasks.First(x => x.Id == t.Id);
                    Assert.False(taskState.IsCompleted, $"Task '{t.Title}' must remain incomplete after opening Tasks review");
                }

                // Test Snooze All action on new window
                var win2 = new DesktopPet.UI.ReminderNotificationWindow(
                    tasks,
                    reminderService,
                    onOpenTasks: () => { });
                win2.SnoozeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                foreach (var t in tasks)
                {
                    var updated = taskService.Tasks.First(x => x.Id == t.Id);
                    Assert.Equal(fixedNow.AddMinutes(15), updated.ReminderAt);
                }

                win2.Close();

                win.Close();
                reminderService.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "Consolidated notification test timed out");
        Assert.Null(threadEx);
    }
}
