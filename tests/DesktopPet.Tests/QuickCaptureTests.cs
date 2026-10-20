using System.IO;
using System.Threading;
using System.Windows.Threading;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class QuickCaptureTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testTasksPath;
    private readonly string _testNotesPath;

    public QuickCaptureTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NekuQuickCaptureTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testTasksPath = Path.Combine(_testDir, "tasks.json");
        _testNotesPath = Path.Combine(_testDir, "notes.json");
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
    public void HotkeyService_QuickCaptureHotkeyId_IsUnique()
    {
        Assert.Equal(0xB005, HotkeyService.QuickCaptureHotkeyId);
        Assert.NotEqual(HotkeyService.SummonHotkeyId, HotkeyService.QuickCaptureHotkeyId);
        Assert.NotEqual(HotkeyService.ExitHotkeyId, HotkeyService.QuickCaptureHotkeyId);
        Assert.NotEqual(HotkeyService.PaletteHotkeyId, HotkeyService.QuickCaptureHotkeyId);
    }

    [Fact]
    public void QuickCaptureWindow_InitialState_DefaultsToTaskMode()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Task);

                Assert.Equal(QuickCaptureMode.Task, win.CurrentMode);
                Assert.Equal(string.Empty, win.InputBox.Text);
                Assert.Contains("Task", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow initial state test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_InitialState_CanStartInNoteMode()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Note);

                Assert.Equal(QuickCaptureMode.Note, win.CurrentMode);
                Assert.Contains("Note", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow Note mode test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_SetMode_UpdatesModeAndUi()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Task);
                Assert.Equal(QuickCaptureMode.Task, win.CurrentMode);

                win.SetMode(QuickCaptureMode.Note);
                Assert.Equal(QuickCaptureMode.Note, win.CurrentMode);
                Assert.Contains("Note", win.FeedbackText.Text);

                win.SetMode(QuickCaptureMode.Task);
                Assert.Equal(QuickCaptureMode.Task, win.CurrentMode);
                Assert.Contains("Task", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow SetMode test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_Validation_RejectsEmptyOrWhitespaceInput()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Task);

                win.InputBox.Text = "   ";
                bool saved = win.ExecuteSave();

                Assert.False(saved);
                Assert.Empty(taskService.Tasks);
                Assert.Contains("Please enter text", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow validation test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_SaveTask_CreatesTaskWithTodayDueDate()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Task);

                win.InputBox.Text = "Finish quarterly review";
                bool saved = win.ExecuteSave();

                Assert.True(saved);
                Assert.Single(taskService.Tasks);
                var task = taskService.Tasks[0];
                Assert.Equal("Finish quarterly review", task.Title);
                Assert.NotNull(task.DueDate);
                Assert.Equal(DateTime.Today, task.DueDate.Value.Date);
                Assert.Contains("saved", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow task save test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_SaveNote_CreatesNoteWithFirstLineTitle()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Note);

                string multiLine = "Project Alpha Roadmap\nMilestone 1: Prototype\nMilestone 2: Beta";
                win.InputBox.Text = multiLine;
                bool saved = win.ExecuteSave();

                Assert.True(saved);
                Assert.Single(notesService.Notes);
                var note = notesService.Notes[0];
                Assert.Equal("Project Alpha Roadmap", note.Title);
                Assert.Equal(multiLine, note.Content);
                Assert.Contains("saved", win.FeedbackText.Text);

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

        Assert.True(finished, "QuickCaptureWindow note save test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_SaveNote_TruncatesLongTitle()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Note);

                string longTitle = new string('A', 80);
                win.InputBox.Text = longTitle;
                bool saved = win.ExecuteSave();

                Assert.True(saved);
                Assert.Single(notesService.Notes);
                var note = notesService.Notes[0];
                Assert.True(note.Title.Length <= 61);
                Assert.EndsWith("…", note.Title);

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

        Assert.True(finished, "QuickCaptureWindow title truncation test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void QuickCaptureWindow_DuplicateSave_IsPreventedByReentrancyGuard()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var taskService = new TaskService(_testTasksPath);
                var notesService = new NotesService(_testNotesPath);

                var win = new QuickCaptureWindow(taskService, notesService, QuickCaptureMode.Task);
                win.InputBox.Text = "Unique Task Once";

                bool firstSave = win.ExecuteSave();
                bool secondSave = win.ExecuteSave();

                Assert.True(firstSave);
                Assert.False(secondSave, "Re-entrant or duplicate save should be rejected");
                Assert.Single(taskService.Tasks);

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

        Assert.True(finished, "QuickCaptureWindow duplicate save test timed out");
        Assert.Null(threadEx);
    }

    [Fact]
    public void HotkeyService_QuickCapture_RegistrationAndUnregistration()
    {
        using var hotkey = new HotkeyService();
        // Unregister without HWND attached should be completely safe and not throw
        hotkey.UnregisterQuickCapture();
        hotkey.Dispose();
    }

    [Fact]
    public void SettingsWindow_QuickCaptureHotkey_ConfiguresAndResetsCorrectly()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settingsPath = Path.Combine(_testDir, "settings.json");
                var service = new SettingsService(settingsPath);
                var settings = service.Current;
                settings.EnableQuickCaptureHotkey = true;
                settings.QuickCaptureHotkeyModifiers = (int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt);
                settings.QuickCaptureHotkeyKey = (int)System.Windows.Input.Key.C;

                var win = new SettingsWindow(settings, service, () => { });

                Assert.True(win.QuickCaptureHotkeyBox.IsChecked);
                Assert.Equal("Ctrl + Alt + C", win.QuickCaptureHotkeyCaptureBox.Text);

                // Modify hotkey programmatically
                settings.QuickCaptureHotkeyModifiers = (int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
                settings.QuickCaptureHotkeyKey = (int)System.Windows.Input.Key.K;
                win.QuickCaptureHotkeyCaptureBox.Text = HotkeyService.FormatHotkey(
                    System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                    System.Windows.Input.Key.K);

                Assert.Equal("Ctrl + Shift + K", win.QuickCaptureHotkeyCaptureBox.Text);

                // Trigger reset
                win.ResetQuickCaptureHotkeyButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal("Ctrl + Alt + C", win.QuickCaptureHotkeyCaptureBox.Text);
                Assert.Equal((int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), settings.QuickCaptureHotkeyModifiers);
                Assert.Equal((int)System.Windows.Input.Key.C, settings.QuickCaptureHotkeyKey);

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

        Assert.True(finished, "SettingsWindow QuickCapture hotkey test timed out");
        Assert.Null(threadEx);
    }
}
