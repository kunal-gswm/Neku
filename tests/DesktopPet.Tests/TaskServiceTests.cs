using System.IO;
using System.Text.Json;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class TaskServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFilePath;

    public TaskServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NekuTaskTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testFilePath = Path.Combine(_testDir, "tasks.json");
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
    public void TaskItem_DefaultValues_AreCorrect()
    {
        var task = new TaskItem();

        Assert.False(string.IsNullOrWhiteSpace(task.Id));
        Assert.Equal("New Task", task.Title);
        Assert.Equal("", task.Description);
        Assert.False(task.IsCompleted);
        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.Null(task.DueDate);
        Assert.Equal(0, task.EstimatedMinutes);
        Assert.Equal(0, task.CompletedPomodoros);
        Assert.Null(task.CompletedAt);
        Assert.False(task.IsOverdue);
        Assert.False(task.IsDueToday);
        Assert.Equal("Normal", task.PriorityDisplay);
        Assert.Equal("", task.PomodoroDisplay);
    }

    [Fact]
    public void TaskItem_IsOverdue_CalculatesCorrectly()
    {
        var task = new TaskItem
        {
            DueDate = DateTime.Today.AddDays(-1),
            IsCompleted = false
        };
        Assert.True(task.IsOverdue);

        // Completed tasks are never overdue
        task.IsCompleted = true;
        Assert.False(task.IsOverdue);

        // Future tasks are not overdue
        task.IsCompleted = false;
        task.DueDate = DateTime.Today.AddDays(1);
        Assert.False(task.IsOverdue);

        // No due date is not overdue
        task.DueDate = null;
        Assert.False(task.IsOverdue);
    }

    [Fact]
    public void TaskItem_IsDueToday_CalculatesCorrectly()
    {
        var task = new TaskItem
        {
            DueDate = DateTime.Today,
            IsCompleted = false
        };
        Assert.True(task.IsDueToday);

        task.IsCompleted = true;
        Assert.False(task.IsDueToday);

        task.DueDate = DateTime.Today.AddDays(1);
        Assert.False(task.IsDueToday);
    }

    [Fact]
    public void TaskItem_PriorityAndPomodoroDisplays_FormatProperly()
    {
        var taskHigh = new TaskItem { Priority = TaskPriority.High, CompletedPomodoros = 3 };
        Assert.Equal("High", taskHigh.PriorityDisplay);
        Assert.Equal("🍅 3", taskHigh.PomodoroDisplay);

        var taskLow = new TaskItem { Priority = TaskPriority.Low, CompletedPomodoros = 0 };
        Assert.Equal("Low", taskLow.PriorityDisplay);
        Assert.Equal("", taskLow.PomodoroDisplay);

        var taskNormal = new TaskItem { Priority = TaskPriority.Normal };
        Assert.Equal("Normal", taskNormal.PriorityDisplay);
    }

    [Fact]
    public void TaskService_Create_AddsTask_AndTriggersEvents()
    {
        var service = new TaskService(_testFilePath);
        bool changedFired = false;
        service.TasksChanged += () => changedFired = true;

        var dueDate = DateTime.Today.AddDays(2);
        var created = service.Create(
            title: "  Fix layout bug  ",
            description: "Check margin and padding",
            priority: TaskPriority.High,
            dueDate: dueDate,
            estimatedMinutes: 50);

        Assert.NotNull(created);
        Assert.Equal("Fix layout bug", created.Title);
        Assert.Equal("Check margin and padding", created.Description);
        Assert.Equal(TaskPriority.High, created.Priority);
        Assert.Equal(dueDate, created.DueDate);
        Assert.Equal(50, created.EstimatedMinutes);
        Assert.Single(service.Tasks);
        Assert.True(changedFired);
        Assert.True(File.Exists(_testFilePath));
    }

    [Fact]
    public void TaskService_Update_ModifiesTask_AndSaves()
    {
        var service = new TaskService(_testFilePath);
        var task = service.Create("Initial title");

        bool updated = service.Update(task.Id, t =>
        {
            t.Title = "Updated title";
            t.Priority = TaskPriority.High;
            t.EstimatedMinutes = 75;
        });

        Assert.True(updated);
        var fetched = service.Get(task.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated title", fetched.Title);
        Assert.Equal(TaskPriority.High, fetched.Priority);
        Assert.Equal(75, fetched.EstimatedMinutes);

        // Non-existent id returns false
        Assert.False(service.Update("non-existent-id", _ => { }));
    }

    [Fact]
    public void TaskService_ToggleComplete_And_Reopen_TriggersProperEvents()
    {
        var service = new TaskService(_testFilePath);
        var task = service.Create("Complete me");

        TaskItem? completedTask = null;
        TaskItem? reopenedTask = null;
        service.TaskCompleted += t => completedTask = t;
        service.TaskReopened += t => reopenedTask = t;

        // 1. Toggle to completed
        bool newState = service.ToggleComplete(task.Id);
        Assert.True(newState);
        Assert.True(task.IsCompleted);
        Assert.NotNull(task.CompletedAt);
        Assert.Same(task, completedTask);

        // 2. Toggle to reopened
        newState = service.ToggleComplete(task.Id);
        Assert.False(newState);
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAt);
        Assert.Same(task, reopenedTask);
    }

    [Fact]
    public void TaskService_Complete_And_Reopen_DirectMethods()
    {
        var service = new TaskService(_testFilePath);
        var task = service.Create("Direct completion test");

        Assert.True(service.Complete(task.Id));
        Assert.True(task.IsCompleted);
        Assert.False(service.Complete(task.Id)); // Already completed -> false

        Assert.True(service.Reopen(task.Id));
        Assert.False(task.IsCompleted);
        Assert.False(service.Reopen(task.Id)); // Already active -> false
    }

    [Fact]
    public void TaskService_Delete_RemovesTask_AndRaisesEvent()
    {
        var service = new TaskService(_testFilePath);
        var task1 = service.Create("Task 1");
        var task2 = service.Create("Task 2");

        TaskItem? deletedTask = null;
        service.TaskDeleted += t => deletedTask = t;

        bool result = service.Delete(task1.Id);
        Assert.True(result);
        Assert.Same(task1, deletedTask);
        Assert.Single(service.Tasks);
        Assert.Null(service.Get(task1.Id));
        Assert.NotNull(service.Get(task2.Id));

        // Deleting non-existent returns false
        Assert.False(service.Delete("non-existent"));
    }

    [Fact]
    public void TaskService_RecordFocusCompleted_IncrementsPomodoros_WithoutCompletingTask()
    {
        var service = new TaskService(_testFilePath);
        var task = service.Create("Focus Task");

        Assert.Equal(0, task.CompletedPomodoros);
        Assert.False(task.IsCompleted);

        bool success = service.RecordFocusCompleted(task.Id);
        Assert.True(success);
        Assert.Equal(1, task.CompletedPomodoros);
        Assert.False(task.IsCompleted); // MUST NOT auto-complete

        service.RecordFocusCompleted(task.Id);
        Assert.Equal(2, task.CompletedPomodoros);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void TaskService_Persistence_SurvivesReloadAcrossInstances()
    {
        var service1 = new TaskService(_testFilePath);
        var t1 = service1.Create("Task Alpha", "Description Alpha", TaskPriority.High, DateTime.Today.AddDays(1), 50);
        var t2 = service1.Create("Task Beta", "Description Beta", TaskPriority.Low, DateTime.Today.AddDays(-2), 25);
        service1.RecordFocusCompleted(t1.Id);
        service1.Complete(t2.Id);

        // Create fresh service loading from same file
        var service2 = new TaskService(_testFilePath);
        Assert.Equal(2, service2.Tasks.Count);

        var loadedT1 = service2.Get(t1.Id);
        Assert.NotNull(loadedT1);
        Assert.Equal("Task Alpha", loadedT1.Title);
        Assert.Equal("Description Alpha", loadedT1.Description);
        Assert.Equal(TaskPriority.High, loadedT1.Priority);
        Assert.Equal(50, loadedT1.EstimatedMinutes);
        Assert.Equal(1, loadedT1.CompletedPomodoros);
        Assert.False(loadedT1.IsCompleted);

        var loadedT2 = service2.Get(t2.Id);
        Assert.NotNull(loadedT2);
        Assert.Equal("Task Beta", loadedT2.Title);
        Assert.Equal(TaskPriority.Low, loadedT2.Priority);
        Assert.True(loadedT2.IsCompleted);
        Assert.NotNull(loadedT2.CompletedAt);
    }

    [Fact]
    public void TaskService_HandlesMalformedAndLegacyJson_Gracefully_AndPreservesCorruptData()
    {
        // 1. Corrupt JSON
        File.WriteAllText(_testFilePath, "{ malformed json content !!! }");
        var serviceCorrupt = new TaskService(_testFilePath);
        Assert.Empty(serviceCorrupt.Tasks);

        // Verify corrupt file was backed up and never silently wiped out
        Assert.NotNull(serviceCorrupt.LastCorruptBackupPath);
        Assert.True(File.Exists(serviceCorrupt.LastCorruptBackupPath));
        Assert.Equal("{ malformed json content !!! }", File.ReadAllText(serviceCorrupt.LastCorruptBackupPath));

        // Adding a new task saves cleanly while keeping the corrupt backup intact
        serviceCorrupt.Create("Recovered Session Task");
        Assert.True(File.Exists(serviceCorrupt.LastCorruptBackupPath));
        Assert.Equal("{ malformed json content !!! }", File.ReadAllText(serviceCorrupt.LastCorruptBackupPath));

        // 2. Legacy array format [ { "id": "123", "title": "Old Task" } ]
        string legacyJson = JsonSerializer.Serialize(new[]
        {
            new TaskItem { Id = "legacy-1", Title = "Legacy Task 1" },
            new TaskItem { Id = "legacy-2", Title = "Legacy Task 2" }
        });
        File.WriteAllText(_testFilePath, legacyJson);

        var serviceLegacy = new TaskService(_testFilePath);
        Assert.Equal(2, serviceLegacy.Tasks.Count);
        Assert.Equal("Legacy Task 1", serviceLegacy.Tasks[0].Title);
        Assert.Equal("Legacy Task 2", serviceLegacy.Tasks[1].Title);
    }

    [Fact]
    public void TaskService_RecoversInterruptedAtomicWrite_WhenPrimaryMissing()
    {
        // Simulate a power cut or crash right after temp write, before File.Move
        string tempFile = _testFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        var doc = new TaskDocument
        {
            Version = 1,
            Tasks = [new TaskItem { Id = "salvaged-1", Title = "Salvaged Task" }]
        };
        File.WriteAllText(tempFile, JsonSerializer.Serialize(doc));

        // Ensure primary file does not exist
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);

        var service = new TaskService(_testFilePath);
        Assert.Single(service.Tasks);
        Assert.Equal("Salvaged Task", service.Tasks[0].Title);
        Assert.True(File.Exists(_testFilePath));
    }

    [Fact]
    public void TaskItem_ToLocalDate_HandlesUtcAndLocalBoundaries()
    {
        // 1. UTC timestamp
        var utcDate = new DateTime(2026, 10, 18, 23, 0, 0, DateTimeKind.Utc);
        var expectedLocal = utcDate.ToLocalTime().Date;
        Assert.Equal(expectedLocal, TaskItem.ToLocalDate(utcDate));

        // 2. Local date
        var localDate = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Local);
        Assert.Equal(new DateTime(2026, 10, 18), TaskItem.ToLocalDate(localDate));

        // 3. Unspecified date (from WPF DatePicker)
        var unspecDate = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2026, 10, 18), TaskItem.ToLocalDate(unspecDate));
    }

    [Fact]
    public void FocusTimerService_ClearActiveTask_NotifiesListeners()
    {
        var timer = new FocusTimerService();
        timer.StartFocus(25, "task-999", "Task To Delete");
        Assert.Equal("task-999", timer.ActiveTaskId);

        bool stateChanged = false;
        timer.StateChanged += _ => stateChanged = true;

        timer.ClearActiveTask();
        Assert.Null(timer.ActiveTaskId);
        Assert.Null(timer.ActiveTaskTitle);
        Assert.True(timer.IsActive); // Timer is still running
        Assert.True(stateChanged);   // HUD listener informed
    }

    [Fact]
    public void TaskService_FilteringAndSorting_OperatesCorrectly()
    {
        var service = new TaskService(_testFilePath);
        var overdue = service.Create("Overdue Task", dueDate: DateTime.Today.AddDays(-2));
        var today = service.Create("Today Task", dueDate: DateTime.Today);
        var upcoming = service.Create("Upcoming Task", dueDate: DateTime.Today.AddDays(3));
        var completed = service.Create("Completed Task");
        service.Complete(completed.Id);

        var tasks = service.Tasks;

        // Today view: due today or overdue, not completed
        var todayTasks = tasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && t.DueDate.Value.Date <= DateTime.Today).ToList();
        Assert.Contains(overdue, todayTasks);
        Assert.Contains(today, todayTasks);
        Assert.DoesNotContain(upcoming, todayTasks);
        Assert.DoesNotContain(completed, todayTasks);

        // Upcoming view: due after today, not completed
        var upcomingTasks = tasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && t.DueDate.Value.Date > DateTime.Today).ToList();
        Assert.Single(upcomingTasks);
        Assert.Equal(upcoming.Id, upcomingTasks[0].Id);

        // Completed view
        var completedTasks = tasks.Where(t => t.IsCompleted).ToList();
        Assert.Single(completedTasks);
        Assert.Equal(completed.Id, completedTasks[0].Id);

        // Search filtering
        var searchResults = tasks.Where(t => t.Title.Contains("Overdue", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(searchResults);
        Assert.Equal(overdue.Id, searchResults[0].Id);
    }

    [Fact]
    public void Personality_TaskCompletedLines_ReturnAppropriateQuips()
    {
        var p = new Personality();

        // Default companion lines
        string defaultLine = p.GetTaskCompletedLine("Finish Docs");
        Assert.False(string.IsNullOrWhiteSpace(defaultLine));

        // Billu companion lines
        p.SetActivePet("cat-billu");
        string billuLine = p.GetTaskCompletedLine("Finish Docs");
        Assert.False(string.IsNullOrWhiteSpace(billuLine));

        // Billu line should come from BilluTaskCompletedLines pool
        Assert.True(p.IsBillu);
    }

    [Fact]
    public void FocusTimerService_ActiveTask_AssociatesAndClearsCorrectly()
    {
        var timer = new FocusTimerService();

        Assert.Null(timer.ActiveTaskId);
        Assert.Null(timer.ActiveTaskTitle);

        timer.StartFocus(25, "task-123", "Write unit tests");
        Assert.Equal("task-123", timer.ActiveTaskId);
        Assert.Equal("Write unit tests", timer.ActiveTaskTitle);
        Assert.True(timer.IsActive);

        var settings = new AppSettings();
        timer.SaveState(settings);
        Assert.Equal("task-123", settings.FocusActiveTaskId);

        // Cancel clears active task
        timer.Cancel();
        Assert.Null(timer.ActiveTaskId);
        Assert.Null(timer.ActiveTaskTitle);
        Assert.False(timer.IsActive);

        // LoadState restores active task id when timer was active
        var timer2 = new FocusTimerService();
        settings.FocusTimerActive = true;
        settings.FocusTimerState = nameof(FocusTimerState.FocusPaused);
        settings.FocusTimerRemainingSeconds = 600;
        settings.FocusActiveTaskId = "task-456";
        timer2.LoadState(settings);
        Assert.Equal("task-456", timer2.ActiveTaskId);

        // When timer was inactive, active task is cleared
        var timer3 = new FocusTimerService();
        settings.FocusTimerActive = false;
        settings.FocusActiveTaskId = "task-456";
        timer3.LoadState(settings);
        Assert.Null(timer3.ActiveTaskId);
    }
}
