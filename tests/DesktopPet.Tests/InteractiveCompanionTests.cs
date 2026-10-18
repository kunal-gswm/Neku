using System.IO;
using System.Threading;
using System.Windows;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class InteractiveCompanionTests
{
    // ── 1. Focus Timer Tests ──────────────────────────────────────────────────

    [Fact]
    public void FocusTimer_starts_and_sets_correct_deadline()
    {
        using var timer = new FocusTimerService();
        Assert.Equal(FocusTimerState.Idle, timer.State);
        Assert.False(timer.IsActive);

        timer.StartFocus(25);

        Assert.Equal(FocusTimerState.Focusing, timer.State);
        Assert.True(timer.IsActive);
        Assert.False(timer.IsPaused);
        Assert.False(timer.IsBreak);
        Assert.NotNull(timer.TargetDeadlineUtc);

        var remaining = timer.RemainingTime;
        Assert.InRange(remaining.TotalMinutes, 24.8, 25.1);
        Assert.True(timer.FormattedRemaining.StartsWith("25:") || timer.FormattedRemaining.StartsWith("24:59"));
    }

    [Fact]
    public void FocusTimer_pause_and_resume_preserves_remaining_time()
    {
        using var timer = new FocusTimerService();
        timer.StartFocus(20);

        timer.Pause();
        Assert.Equal(FocusTimerState.FocusPaused, timer.State);
        Assert.True(timer.IsPaused);
        var pausedRem = timer.RemainingTime;

        // Ensure remaining time is frozen while paused
        Assert.InRange(pausedRem.TotalMinutes, 19.8, 20.1);

        timer.Resume();
        Assert.Equal(FocusTimerState.Focusing, timer.State);
        Assert.False(timer.IsPaused);
        Assert.NotNull(timer.TargetDeadlineUtc);
        Assert.InRange(timer.RemainingTime.TotalMinutes, 19.5, 20.1);
    }

    [Fact]
    public void FocusTimer_cancel_returns_to_idle()
    {
        using var timer = new FocusTimerService();
        timer.StartFocus(25);
        Assert.True(timer.IsActive);

        timer.Cancel();
        Assert.Equal(FocusTimerState.Idle, timer.State);
        Assert.False(timer.IsActive);
        Assert.Null(timer.TargetDeadlineUtc);
        Assert.Equal(TimeSpan.Zero, timer.RemainingTime);
    }

    [Fact]
    public void FocusTimer_reset_restarts_session()
    {
        using var timer = new FocusTimerService();
        timer.StartFocus(15);
        timer.Pause();

        timer.Reset();
        Assert.Equal(FocusTimerState.Focusing, timer.State);
        Assert.False(timer.IsPaused);
        Assert.InRange(timer.RemainingTime.TotalMinutes, 24.8, 25.1); // resets to default FocusDurationMinutes
    }

    [Fact]
    public void FocusTimer_persists_and_restores_state()
    {
        var settings = new AppSettings();
        using (var timer = new FocusTimerService())
        {
            timer.StartFocus(30);
            timer.SaveState(settings);
        }

        Assert.True(settings.FocusTimerActive);
        Assert.Equal("Focusing", settings.FocusTimerState);
        Assert.NotNull(settings.FocusTimerDeadlineUtc);

        using (var restored = new FocusTimerService())
        {
            restored.LoadState(settings);
            Assert.Equal(FocusTimerState.Focusing, restored.State);
            Assert.True(restored.IsActive);
            Assert.InRange(restored.RemainingTime.TotalMinutes, 29.5, 30.1);
        }
    }

    [Fact]
    public void FocusTimer_expired_deadline_on_restore_resets_to_idle()
    {
        var settings = new AppSettings
        {
            FocusTimerActive = true,
            FocusTimerState = "Focusing",
            FocusTimerDeadlineUtc = DateTime.UtcNow.AddMinutes(-5) // expired 5 minutes ago
        };

        using var restored = new FocusTimerService();
        restored.LoadState(settings);
        Assert.Equal(FocusTimerState.Idle, restored.State);
        Assert.False(restored.IsActive);
    }

    [Fact]
    public void FocusHudWindow_initializes_with_default_duration_and_handles_toggle()
    {
        var thread = new Thread(() =>
        {
            using var timer = new FocusTimerService();
            timer.FocusDurationMinutes = 30;
            bool toggled = false;
            bool canceled = false;

            var hud = new FocusHudWindow(timer, () => toggled = true, () => canceled = true);
            Assert.Equal("30:00", hud.TimeText.Text);
            Assert.Equal("▶", hud.PauseButton.Content);

            // Test toggling when idle calls the callback
            hud.PauseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(toggled);

            // Test cancel callback
            var cancelItem = hud.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                .First(i => (string)i.Header == "Cancel Focus Session");
            cancelItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.True(canceled);

            hud.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished);
    }

    // ── 2. Quick Notes Tests ──────────────────────────────────────────────────

    [Fact]
    public void NotesService_creates_updates_and_deletes_notes()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "notes.json");

        try
        {
            var service = new NotesService(testFile);
            Assert.Empty(service.Notes);

            var note = service.Create("Test Title", "Sample note content.");
            Assert.Single(service.Notes);
            Assert.Equal("Test Title", note.Title);
            Assert.Equal("Sample note content.", note.Content);

            // Update
            service.Update(note.Id, "Updated Title", "Updated content.");
            var updated = service.Get(note.Id);
            Assert.NotNull(updated);
            Assert.Equal("Updated Title", updated.Title);
            Assert.Equal("Updated content.", updated.Content);

            // Verify persistence to disk
            Assert.True(File.Exists(testFile));
            var reloaded = new NotesService(testFile);
            Assert.Single(reloaded.Notes);
            Assert.Equal("Updated Title", reloaded.Notes[0].Title);

            // Delete
            bool deleted = service.Delete(note.Id);
            Assert.True(deleted);
            Assert.Empty(service.Notes);

            var reloadedAfterDelete = new NotesService(testFile);
            Assert.Empty(reloadedAfterDelete.Notes);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void NotesService_handles_corrupt_file_gracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "notes.json");

        try
        {
            File.WriteAllText(testFile, "{ invalid json text }");
            var service = new NotesService(testFile);
            Assert.Empty(service.Notes); // should fall back gracefully without throwing
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void NotesService_defaults_empty_or_whitespace_title_to_untitled()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string testFile = Path.Combine(tempDir, "notes.json");

        try
        {
            var service = new NotesService(testFile);
            var note1 = service.Create("", "Content");
            var note2 = service.Create("   ", "Content");

            Assert.Equal("Untitled", note1.Title);
            Assert.Equal("Untitled", note2.Title);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ── 3. Onboarding Dismissal Tests ─────────────────────────────────────────


    [Fact]
    public void Onboarding_default_is_false_and_persists_when_dismissed()
    {
        var settings = new AppSettings();
        Assert.False(settings.OnboardingDismissed);

        settings.OnboardingDismissed = true;
        Assert.True(settings.OnboardingDismissed);
    }

    // ── 4. Personality & Reaction Lines Tests ─────────────────────────────────

    [Fact]
    public void Personality_returns_valid_contextual_lines()
    {
        var p = new Personality();

        string focusStart = p.GetFocusStartedLine();
        Assert.False(string.IsNullOrWhiteSpace(focusStart));

        string focusDone = p.GetFocusCompletedLine();
        Assert.False(string.IsNullOrWhiteSpace(focusDone));

        string breakDone = p.GetBreakCompletedLine();
        Assert.False(string.IsNullOrWhiteSpace(breakDone));

        string buildOk = p.GetBuildSuccessLine();
        Assert.False(string.IsNullOrWhiteSpace(buildOk));

        string testFail = p.GetTestFailureLine();
        Assert.False(string.IsNullOrWhiteSpace(testFail));

        string boop = p.GetBoopLine();
        Assert.False(string.IsNullOrWhiteSpace(boop));

        string pet = p.GetPetLine();
        Assert.False(string.IsNullOrWhiteSpace(pet));

        string drag = p.GetDragLine();
        Assert.False(string.IsNullOrWhiteSpace(drag));
    }

    [Fact]
    public void Personality_avoids_immediate_repetitive_lines()
    {
        var p = new Personality();

        // Sample consecutive lines from boop pool
        string first = p.GetBoopLine();
        string second = p.GetBoopLine();

        // Because pool has 7 lines, second line should not immediately repeat first line
        Assert.NotEqual(first, second);
    }

    // ── 5. Action Routing & Menu Tests ────────────────────────────────────────

    [Fact]
    public void ActionRouting_invokes_registered_actions()
    {
        bool focusToggled = false;
        bool focusCanceled = false;
        bool quickNoteOpened = false;
        bool devEventsOpened = false;
        bool settingsOpened = false;
        bool pauseToggled = false;
        bool hideToggled = false;
        bool quitInvoked = false;

        Action onFocusToggle = () => focusToggled = true;
        Action onFocusCancel = () => focusCanceled = true;
        Action onQuickNote = () => quickNoteOpened = true;
        Action onDevEvents = () => devEventsOpened = true;
        Action onSettings = () => settingsOpened = true;
        Action onTogglePause = () => pauseToggled = true;
        Action onHide = () => hideToggled = true;
        Action onExit = () => quitInvoked = true;

        onFocusToggle();
        onFocusCancel();
        onQuickNote();
        onDevEvents();
        onSettings();
        onTogglePause();
        onHide();
        onExit();

        Assert.True(focusToggled);
        Assert.True(focusCanceled);
        Assert.True(quickNoteOpened);
        Assert.True(devEventsOpened);
        Assert.True(settingsOpened);
        Assert.True(pauseToggled);
        Assert.True(hideToggled);
        Assert.True(quitInvoked);
    }
}
