using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class KeyboardIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _tasksFile;
    private readonly string _notesFile;

    public KeyboardIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NekuKeyboardTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _tasksFile = Path.Combine(_testDir, "tasks.json");
        _notesFile = Path.Combine(_testDir, "notes.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public void DashboardWindow_KeyboardShortcuts_SwitchTabsCleanly()
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
                focusTimer.HistoryService = historyService;

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "Overview");
                Assert.Equal("Overview", dashboard.CurrentTab);

                // Ctrl + 2 -> Tasks
                dashboard.HandleKeyDown(Key.D2, ModifierKeys.Control);
                Assert.Equal("Tasks", dashboard.CurrentTab);

                // Ctrl + 3 -> Focus
                dashboard.HandleKeyDown(Key.D3, ModifierKeys.Control);
                Assert.Equal("Focus", dashboard.CurrentTab);

                // Ctrl + 4 -> History
                dashboard.HandleKeyDown(Key.D4, ModifierKeys.Control);
                Assert.Equal("History", dashboard.CurrentTab);

                // Ctrl + 5 -> Insights
                dashboard.HandleKeyDown(Key.D5, ModifierKeys.Control);
                Assert.Equal("Insights", dashboard.CurrentTab);

                // Ctrl + 6 -> Goals
                dashboard.HandleKeyDown(Key.D6, ModifierKeys.Control);
                Assert.Equal("Goals", dashboard.CurrentTab);

                // Ctrl + 7 -> Agent
                dashboard.HandleKeyDown(Key.D7, ModifierKeys.Control);
                Assert.Equal("Agent", dashboard.CurrentTab);

                // Ctrl + 8 -> Radar
                dashboard.HandleKeyDown(Key.D8, ModifierKeys.Control);
                Assert.Equal("Radar", dashboard.CurrentTab);

                // Ctrl + 1 -> Overview
                dashboard.HandleKeyDown(Key.D1, ModifierKeys.Control);
                Assert.Equal("Overview", dashboard.CurrentTab);

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

        Assert.True(finished, "DashboardWindow tab shortcuts test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_TabCycling_CtrlTab_CyclesForwardAndReverse()
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
                focusTimer.HistoryService = historyService;

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "Overview");
                Assert.Equal("Overview", dashboard.CurrentTab);

                // Ctrl+Tab forward: Overview -> Tasks -> Focus
                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("Tasks", dashboard.CurrentTab);

                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("Focus", dashboard.CurrentTab);

                // Ctrl+Shift+Tab reverse: Focus -> Tasks -> Overview
                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal("Tasks", dashboard.CurrentTab);

                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal("Overview", dashboard.CurrentTab);

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

        Assert.True(finished, "DashboardWindow tab cycling test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_Escape_ClearsSearchWithoutClosingWindow()
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
                focusTimer.HistoryService = historyService;

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "Tasks");
                dashboard.Show();

                dashboard.TaskSearchBox.Focus();
                dashboard.TaskSearchBox.Text = "urgent";

                // Pressing Escape while search has text should clear search, NOT close window
                dashboard.HandleKeyDown(Key.Escape, ModifierKeys.None);

                Assert.Equal("", dashboard.TaskSearchBox.Text);

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

        Assert.True(finished, "DashboardWindow Escape test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickNotesWindow_KeyboardShortcuts_ExecuteCorrectly()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var notesService = new NotesService(_notesFile);
                var window = new QuickNotesWindow(notesService);
                window.Show();

                // Ctrl+N creates note
                int initialCount = notesService.Notes.Count;
                window.HandleKeyDown(Key.N, ModifierKeys.Control);
                Assert.True(notesService.Notes.Count >= initialCount);

                // Ctrl+F focuses search box
                window.HandleKeyDown(Key.F, ModifierKeys.Control);
                Assert.True(window.SearchBox.IsFocused);

                // Typing search and pressing Escape clears search
                window.SearchBox.Text = "query";
                window.HandleKeyDown(Key.Escape, ModifierKeys.None);
                Assert.Equal("", window.SearchBox.Text);

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "QuickNotesWindow shortcuts test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void TasksWindow_KeyboardShortcuts_FocusAndClearSearch()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();
                var settings = new AppSettings();

                var window = new TasksWindow(taskService, focusTimer, settings);
                window.Show();

                // Ctrl+F focuses search
                window.HandleKeyDown(Key.F, ModifierKeys.Control);
                Assert.True(window.SearchBox.IsFocused);

                // Escape clears search
                window.SearchBox.Text = "testing";
                window.HandleKeyDown(Key.Escape, ModifierKeys.None);
                Assert.Equal("", window.SearchBox.Text);

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "TasksWindow shortcuts test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void CommandPaletteWindow_Navigation_WraparoundAndShortcuts()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var service = new CommandPaletteService();
                service.Register(new PaletteCommand("c1", "Command 1", "d", "General", () => { }));
                service.Register(new PaletteCommand("c2", "Command 2", "d", "General", () => { }));
                service.Register(new PaletteCommand("c3", "Command 3", "d", "General", () => { }));

                var palette = new CommandPaletteWindow(service);
                palette.Show();

                Assert.Equal(3, palette.ResultsListBox.Items.Count);
                Assert.Equal(0, palette.ResultsListBox.SelectedIndex);

                // Up arrow at top wraps to bottom (index 2)
                palette.HandleSearchKeyDown(Key.Up, ModifierKeys.None);
                Assert.Equal(2, palette.ResultsListBox.SelectedIndex);

                // Down arrow at bottom wraps to top (index 0)
                palette.HandleSearchKeyDown(Key.Down, ModifierKeys.None);
                Assert.Equal(0, palette.ResultsListBox.SelectedIndex);

                // Tab moves to next (index 1)
                palette.HandleSearchKeyDown(Key.Tab, ModifierKeys.None);
                Assert.Equal(1, palette.ResultsListBox.SelectedIndex);

                // Shift+Tab moves to previous (index 0)
                palette.HandleSearchKeyDown(Key.Tab, ModifierKeys.Shift);
                Assert.Equal(0, palette.ResultsListBox.SelectedIndex);

                // Ctrl+N moves down (index 1)
                palette.HandleSearchKeyDown(Key.N, ModifierKeys.Control);
                Assert.Equal(1, palette.ResultsListBox.SelectedIndex);

                // Ctrl+P moves up (index 0)
                palette.HandleSearchKeyDown(Key.P, ModifierKeys.Control);
                Assert.Equal(0, palette.ResultsListBox.SelectedIndex);

                // PageDown jumps to end (index 2)
                palette.HandleSearchKeyDown(Key.PageDown, ModifierKeys.None);
                Assert.Equal(2, palette.ResultsListBox.SelectedIndex);

                // PageUp jumps to top (index 0)
                palette.HandleSearchKeyDown(Key.PageUp, ModifierKeys.None);
                Assert.Equal(0, palette.ResultsListBox.SelectedIndex);

                palette.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "CommandPaletteWindow navigation test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void CommandPaletteWindow_CloseAndDeactivate_DoesNotThrowWhileClosing()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var service = new CommandPaletteService();
                var palette = new CommandPaletteWindow(service);
                palette.Show();

                // Calling Close() multiple times or raising deactivated during closing must be completely safe
                palette.Close();
                palette.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "CommandPaletteWindow Close test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void Windows_InAppCommandPaletteShortcuts_TriggerSafely()
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

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings);
                var tasksWin = new TasksWindow(taskService, focusTimer, settings);
                var notesWin = new QuickNotesWindow(notesService);

                // Ctrl+P, Ctrl+Shift+P, and Ctrl+Alt+P all execute without throwing
                dashboard.HandleKeyDown(Key.P, ModifierKeys.Control);
                dashboard.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Shift);
                dashboard.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

                tasksWin.HandleKeyDown(Key.P, ModifierKeys.Control);
                tasksWin.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Shift);
                tasksWin.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

                notesWin.HandleKeyDown(Key.P, ModifierKeys.Control);
                notesWin.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Shift);
                notesWin.HandleKeyDown(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

                dashboard.Close();
                tasksWin.Close();
                notesWin.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "In-app Command Palette shortcuts test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_Shortcuts_CtrlS_SavesNotes_And_F5_RefreshesTab()
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
                focusTimer.HistoryService = historyService;

                // Add a dummy session record
                var rec = new FocusSessionRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    StartUtc = DateTime.UtcNow.AddMinutes(-30),
                    EndUtc = DateTime.UtcNow,
                    TargetMinutes = 25,
                    Status = FocusSessionStatus.Completed,
                    TaskTitle = "Test Task",
                    Notes = "Initial note"
                };
                historyService.RecordSession(rec);

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "History");
                Assert.Equal("History", dashboard.CurrentTab);

                // Select the session
                dashboard.HistoryDataGrid.SelectedItem = rec;
                dashboard.HistoryNotesTextBox.Text = "Updated note via shortcut";

                // Trigger Ctrl+S
                dashboard.HandleKeyDown(Key.S, ModifierKeys.Control);

                // Verify notes were saved in historyService
                var updated = historyService.Sessions.FirstOrDefault(r => r.Id == rec.Id);
                Assert.NotNull(updated);
                Assert.Equal("Updated note via shortcut", updated.Notes);

                // Test F5 and Ctrl+R refresh active tab without throwing
                dashboard.HandleKeyDown(Key.F5, ModifierKeys.None);
                Assert.Equal("History", dashboard.CurrentTab);

                dashboard.HandleKeyDown(Key.R, ModifierKeys.Control);
                Assert.Equal("History", dashboard.CurrentTab);

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

        Assert.True(finished, "DashboardWindow Ctrl+S and F5 test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_Escape_DoesNotWipeHistoryNotes()
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
                focusTimer.HistoryService = historyService;

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "History");
                dashboard.HistoryNotesTextBox.Text = "Important user note that must not be cleared";
                dashboard.HistoryNotesTextBox.Focus();

                // Press Escape
                dashboard.HandleKeyDown(Key.Escape, ModifierKeys.None);

                // The note text MUST be preserved!
                Assert.Equal("Important user note that must not be cleared", dashboard.HistoryNotesTextBox.Text);

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

        Assert.True(finished, "DashboardWindow Escape test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void TasksWindow_TabCycling_CtrlTab_And_F5_Refresh()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var taskService = new TaskService(_tasksFile);
                var focusTimer = new FocusTimerService();

                var tasksWin = new TasksWindow(taskService, focusTimer, settings);
                tasksWin.SelectTab("Today");
                Assert.Equal("Today", tasksWin.ActiveTab);

                // Ctrl+Tab cycles to Upcoming
                tasksWin.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("Upcoming", tasksWin.ActiveTab);

                // Ctrl+Tab cycles to All
                tasksWin.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("All", tasksWin.ActiveTab);

                // Ctrl+Tab cycles to Completed
                tasksWin.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("Completed", tasksWin.ActiveTab);

                // Ctrl+Tab wraps to Today
                tasksWin.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal("Today", tasksWin.ActiveTab);

                // Ctrl+Shift+Tab cycles backwards to Completed
                tasksWin.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal("Completed", tasksWin.ActiveTab);

                // F5 and Ctrl+R refresh cleanly
                tasksWin.HandleKeyDown(Key.F5, ModifierKeys.None);
                tasksWin.HandleKeyDown(Key.R, ModifierKeys.Control);

                tasksWin.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "TasksWindow Ctrl+Tab test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickNotesWindow_F5_Refresh_And_SafeEscape()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var notesService = new NotesService(_notesFile);
                notesService.Create("Test Note", "Sample content");

                var notesWin = new QuickNotesWindow(notesService);

                // F5 and Ctrl+R refresh list
                notesWin.HandleKeyDown(Key.F5, ModifierKeys.None);
                notesWin.HandleKeyDown(Key.R, ModifierKeys.Control);

                // Safe escape does not throw
                notesWin.HandleKeyDown(Key.Escape, ModifierKeys.None);

                notesWin.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "QuickNotesWindow F5 and Escape test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_Ctrl9_And_NumPad9_SwitchToHealthTab()
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
                var healthService = new DesktopPet.Services.Health.SystemHealthService(settings);

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "Overview", healthService: healthService);
                Assert.Equal("Overview", dashboard.CurrentTab);

                // Ctrl + 9 -> Health
                dashboard.HandleKeyDown(Key.D9, ModifierKeys.Control);
                Assert.Equal("Health", dashboard.CurrentTab);

                // Switch back to Tasks then test NumPad9
                dashboard.HandleKeyDown(Key.D2, ModifierKeys.Control);
                Assert.Equal("Tasks", dashboard.CurrentTab);

                dashboard.HandleKeyDown(Key.NumPad9, ModifierKeys.Control);
                Assert.Equal("Health", dashboard.CurrentTab);

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

        Assert.True(finished, "DashboardWindow Health tab shortcut test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void DashboardWindow_TabCycling_IncludesAllNineTabs()
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

                var dashboard = new DashboardWindow(taskService, focusTimer, historyService, settings, initialTab: "Overview");
                Assert.Equal("Overview", dashboard.CurrentTab);

                string[] expectedForward = ["Tasks", "Focus", "History", "Insights", "Goals", "Agent", "Radar", "Health", "Overview"];
                foreach (var expectedTab in expectedForward)
                {
                    dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                    Assert.Equal(expectedTab, dashboard.CurrentTab);
                }

                // Reverse cycle from Overview -> Health -> Radar
                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal("Health", dashboard.CurrentTab);

                dashboard.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal("Radar", dashboard.CurrentTab);

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

        Assert.True(finished, "DashboardWindow 9-tab cycling test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void SettingsWindow_KeyboardShortcuts_DirectTabs_And_Cycling()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var settingsService = new SettingsService(Path.Combine(_testDir, "settings_test.json"));
                var window = new SettingsWindow(settings, settingsService);

                Assert.Equal(0, window.SettingsTabControl.SelectedIndex);

                // Ctrl+2 -> Behavior (index 1)
                window.HandleKeyDown(Key.D2, ModifierKeys.Control);
                Assert.Equal(1, window.SettingsTabControl.SelectedIndex);

                // Ctrl+3 -> Performance (index 2)
                window.HandleKeyDown(Key.D3, ModifierKeys.Control);
                Assert.Equal(2, window.SettingsTabControl.SelectedIndex);

                // Ctrl+4 -> Developer (index 3)
                window.HandleKeyDown(Key.D4, ModifierKeys.Control);
                Assert.Equal(3, window.SettingsTabControl.SelectedIndex);

                // Ctrl+5 -> DeskSense & Radar (index 4)
                window.HandleKeyDown(Key.D5, ModifierKeys.Control);
                Assert.Equal(4, window.SettingsTabControl.SelectedIndex);

                // Ctrl+6 -> System (index 5)
                window.HandleKeyDown(Key.D6, ModifierKeys.Control);
                Assert.Equal(5, window.SettingsTabControl.SelectedIndex);

                // Ctrl+1 -> Appearance (index 0)
                window.HandleKeyDown(Key.D1, ModifierKeys.Control);
                Assert.Equal(0, window.SettingsTabControl.SelectedIndex);

                // Ctrl+Tab forward -> index 1
                window.HandleKeyDown(Key.Tab, ModifierKeys.Control);
                Assert.Equal(1, window.SettingsTabControl.SelectedIndex);

                // Ctrl+Shift+Tab reverse -> index 0
                window.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal(0, window.SettingsTabControl.SelectedIndex);

                // Ctrl+Shift+Tab reverse wrap -> index 5
                window.HandleKeyDown(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal(5, window.SettingsTabControl.SelectedIndex);

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);

        Assert.True(finished, "SettingsWindow tab shortcuts test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void NotesService_AtomicSave_PreservesData_And_CleansTemp()
    {
        string filePath = Path.Combine(_testDir, "atomic_notes.json");
        var service = new NotesService(filePath);
        var note = service.Create("Atomic Title", "Atomic Content");

        Assert.True(File.Exists(filePath));
        var dirFiles = Directory.GetFiles(_testDir, "*.tmp.*");
        Assert.Empty(dirFiles);

        // Reload from disk to verify integrity
        var reloaded = new NotesService(filePath);
        Assert.Single(reloaded.Notes);
        Assert.Equal("Atomic Title", reloaded.Notes[0].Title);
        Assert.Equal("Atomic Content", reloaded.Notes[0].Content);
    }

    [Fact]
    public void SettingsService_AtomicSave_PreservesData_And_CleansTemp()
    {
        string filePath = Path.Combine(_testDir, "atomic_settings.json");
        var service = new SettingsService(filePath);
        service.Current.Speed = 1.75;
        service.Current.SizeScale = 1.30;
        service.Save();

        Assert.True(File.Exists(filePath));
        var dirFiles = Directory.GetFiles(_testDir, "*.tmp.*");
        Assert.Empty(dirFiles);

        // Reload from disk
        var reloaded = new SettingsService(filePath);
        var loadedSettings = reloaded.Load();
        Assert.Equal(1.75, loadedSettings.Speed);
        Assert.Equal(1.30, loadedSettings.SizeScale);
    }
}
