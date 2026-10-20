using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class EverydayUsabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _notesFile;
    private readonly string _tasksFile;

    public EverydayUsabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neku_usability_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _notesFile = Path.Combine(_tempDir, "notes.json");
        _tasksFile = Path.Combine(_tempDir, "tasks.json");
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
    public void NotesService_NotesChanged_FiresOnCreateUpdateDelete()
    {
        var notesService = new NotesService(_notesFile);
        int changeCount = 0;
        notesService.NotesChanged += () => changeCount++;

        // 1. Create
        var note = notesService.Create("Shopping List", "Milk\nBread\nEggs");
        Assert.Equal(1, changeCount);
        Assert.Single(notesService.Notes);

        // 2. Update
        notesService.Update(note.Id, "Weekly Shopping", "Milk\nBread\nButter");
        Assert.Equal(2, changeCount);

        // 3. Delete
        bool deleted = notesService.Delete(note.Id);
        Assert.True(deleted);
        Assert.Equal(3, changeCount);
        Assert.Empty(notesService.Notes);
    }

    [Fact]
    public void DisplayModels_FormatPrioritiesAndNotePreviewsCorrectly()
    {
        var taskHigh = new TaskItem { Title = "Urgent Client Fix", Priority = TaskPriority.High, Project = "Work" };
        var taskNormal = new TaskItem { Title = "Write Documentation", Priority = TaskPriority.Normal };
        var taskLow = new TaskItem { Title = "Clean Workspace", Priority = TaskPriority.Low };

        var displayHigh = new TodayTaskDisplayItem(taskHigh);
        Assert.Equal("Urgent Client Fix", displayHigh.Title);
        Assert.Equal("● High", displayHigh.PriorityDisplay);
        Assert.Equal("[Work]", displayHigh.ProjectDisplay);

        var displayNormal = new TodayTaskDisplayItem(taskNormal);
        Assert.Equal("● Normal", displayNormal.PriorityDisplay);
        Assert.Equal("", displayNormal.ProjectDisplay);

        var displayLow = new TodayTaskDisplayItem(taskLow);
        Assert.Equal("● Low", displayLow.PriorityDisplay);

        // Note preview model
        var noteLong = new NoteItem
        {
            Title = "Sprint Ideas",
            Content = "This is a very long first line of text that exceeds forty characters and should be truncated with an ellipsis smoothly."
        };
        var noteDisplay = new NoteOverviewItem(noteLong);
        Assert.Equal("Sprint Ideas", noteDisplay.Title);
        Assert.EndsWith("…", noteDisplay.Preview);
        Assert.True(noteDisplay.Preview.Length <= 42);

        var emptyNote = new NoteItem { Title = "", Content = "" };
        var emptyDisplay = new NoteOverviewItem(emptyNote);
        Assert.Equal("Untitled", emptyDisplay.Title);
        Assert.Equal("Empty note", emptyDisplay.Preview);
    }

    [Fact]
    public void DashboardWindow_NavRail_HidesDeveloperButtonsWhenDisabled()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings
                {
                    EnableDeveloperEvents = false,
                    EnableDeveloperRadar = false,
                    EnableAgentCompanion = false
                };
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var historyService = new FocusHistoryService();
                var notesService = new NotesService(_notesFile);

                var dashboard = new DashboardWindow(
                    taskService, focusTimer, historyService, settings,
                    initialTab: "Overview", notesService: notesService);

                Assert.Equal(Visibility.Collapsed, dashboard.NavAgentBtn.Visibility);
                Assert.Equal(Visibility.Collapsed, dashboard.NavRadarBtn.Visibility);
                Assert.Equal(Visibility.Visible, dashboard.NavNotesBtn.Visibility);
                Assert.Equal(Visibility.Visible, dashboard.NavTasksBtn.Visibility);
                Assert.Equal(Visibility.Visible, dashboard.NavOverviewBtn.Visibility);

                dashboard.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow nav rail test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_NavRail_ShowsDeveloperButtonsWhenEnabled()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings
                {
                    EnableDeveloperEvents = true,
                    EnableDeveloperRadar = true,
                    EnableAgentCompanion = true
                };
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var historyService = new FocusHistoryService();

                var dashboard = new DashboardWindow(
                    taskService, focusTimer, historyService, settings,
                    initialTab: "Overview");

                Assert.Equal(Visibility.Visible, dashboard.NavAgentBtn.Visibility);
                Assert.Equal(Visibility.Visible, dashboard.NavRadarBtn.Visibility);

                dashboard.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow developer nav rail test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void TasksWindow_InlineQuickTaskBox_CreatesTaskAndUpdatesList()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();

                var win = new TasksWindow(taskService, focusTimer, settings);
                Assert.NotNull(win.QuickTaskBox);
                Assert.NotNull(win.QuickAddBtn);

                // Switch to Today tab and enter a quick task
                win.SelectTab("Today");
                win.QuickTaskBox.Text = "Ship v1.0.2 release";
                win.QuickAddBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Single(taskService.Tasks);
                var created = taskService.Tasks[0];
                Assert.Equal("Ship v1.0.2 release", created.Title);
                Assert.Equal(DateTime.Today, created.DueDate?.Date);
                Assert.Equal("", win.QuickTaskBox.Text);

                win.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "TasksWindow quick add test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void TrayService_ToggleFocusHudAndDoubleClick_CallbackInvoked()
    {
        var settings = new AppSettings();
        bool hudToggled = false;
        bool doubleClicked = false;

        using var tray = new TrayService(
            onFocusToggle: () => { },
            getFocusLabel: () => "Focus",
            onFocusCancel: () => { },
            isFocusActive: () => false,
            onCommandPalette: () => { },
            onProductivityHub: () => { },
            onTasks: () => { },
            onQuickNote: () => { },
            onDeveloperEvents: () => { },
            onCompanionStudio: () => { },
            onSettings: () => { },
            onPauseToggled: _ => { },
            onHideToggled: _ => { },
            onClickThroughToggled: _ => { },
            onReducedMotionToggled: _ => { },
            onQuietModeToggled: _ => { },
            onResetPosition: () => { },
            onQuit: () => { },
            onUpdate: () => { },
            settings: settings,
            onToggleFocusHud: () => hudToggled = true,
            onDoubleClick: () => doubleClicked = true);

        Assert.NotNull(tray);
        Assert.True(tray.IsFocusHudVisible);
        Assert.False(hudToggled);
        tray.SimulateDoubleClick();
        Assert.True(doubleClicked);
    }

    [Fact]
    public void DashboardWindow_OverviewInteractions_CompletingTask_UpdatesOverviewAndTaskService()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var historyService = new FocusHistoryService();
                var notesService = new NotesService(_notesFile);

                var task = taskService.Create(title: "Finish report", dueDate: DateTime.Today);

                var dashboard = new DashboardWindow(
                    taskService, focusTimer, historyService, settings,
                    initialTab: "Overview", notesService: notesService);

                Assert.Single(taskService.Tasks, t => !t.IsCompleted);

                // Simulate clicking task completion from Overview
                var items = dashboard.OverviewTodayListBox.ItemsSource as System.Collections.Generic.List<TodayTaskDisplayItem>;
                Assert.NotNull(items);
                Assert.Single(items!);

                var displayItem = items![0];
                var cb = new CheckBox { DataContext = displayItem };
                dashboard.OverviewTaskCheck_Click(cb, new RoutedEventArgs());

                // Verify task is now completed
                Assert.True(taskService.Get(task.Id)!.IsCompleted);
                Assert.DoesNotContain(taskService.Tasks, t => !t.IsCompleted);

                dashboard.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow task completion test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_OverviewInteractions_FocusButton_TriggersTaskFocusRequested()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var historyService = new FocusHistoryService();

                var task = taskService.Create(title: "Code review", dueDate: DateTime.Today);

                var dashboard = new DashboardWindow(
                    taskService, focusTimer, historyService, settings,
                    initialTab: "Overview");

                TaskItem? requestedTask = null;
                dashboard.TaskFocusRequested += t => requestedTask = t;

                var items = dashboard.OverviewTodayListBox.ItemsSource as System.Collections.Generic.List<TodayTaskDisplayItem>;
                Assert.NotNull(items);
                var displayItem = items![0];

                var btn = new Button { DataContext = displayItem };
                dashboard.OverviewTaskFocus_Click(btn, new RoutedEventArgs());

                Assert.NotNull(requestedTask);
                Assert.Equal("Code review", requestedTask!.Title);

                dashboard.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow task focus requested test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_OverviewInteractions_QuickNotesSync_UpdatesOnNotesChanged()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var historyService = new FocusHistoryService();
                var notesService = new NotesService(_notesFile);

                var dashboard = new DashboardWindow(
                    taskService, focusTimer, historyService, settings,
                    initialTab: "Overview", notesService: notesService);

                // Initial empty state
                Assert.Equal(Visibility.Visible, dashboard.OverviewNotesEmptyBorder.Visibility);

                // Create a note via service
                notesService.Create("Daily Standup", "Discussing v1.0.2 polish items");

                // Process dispatcher queue to ensure OnNotesChanged runs
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                Assert.Equal(Visibility.Collapsed, dashboard.OverviewNotesEmptyBorder.Visibility);
                var noteItems = dashboard.OverviewNotesListBox.ItemsSource as System.Collections.Generic.List<NoteOverviewItem>;
                Assert.NotNull(noteItems);
                Assert.Single(noteItems!);
                Assert.Equal("Daily Standup", noteItems![0].Title);

                dashboard.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "DashboardWindow quick notes sync test timed out");
        Assert.Null(threadEx);
    }
}
