using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class CommandPaletteTests
{
    [Fact]
    public void PaletteCommand_Initialization_PropertiesSetCorrectly()
    {
        bool executed = false;
        var cmd = new PaletteCommand(
            id: "test.cmd",
            title: "Test Command",
            description: "A test command description",
            category: "Productivity",
            action: () => executed = true,
            icon: "🎯",
            shortcutHint: "Ctrl+P",
            keywords: new[] { "test", "sample" });

        Assert.Equal("test.cmd", cmd.Id);
        Assert.Equal("Test Command", cmd.Title);
        Assert.Equal("A test command description", cmd.Description);
        Assert.Equal("Productivity", cmd.Category);
        Assert.Equal("🎯", cmd.Icon);
        Assert.Equal("Ctrl+P", cmd.ShortcutHint);
        Assert.True(cmd.HasShortcutHint);
        Assert.True(cmd.IsExecutable);
        Assert.Contains("test", cmd.Keywords);

        cmd.Action();
        Assert.True(executed);
    }

    [Fact]
    public void PaletteCommand_CanExecute_ControlsIsExecutable()
    {
        bool allowed = false;
        var cmd = new PaletteCommand(
            id: "test.conditional",
            title: "Conditional",
            description: "desc",
            category: "General",
            action: () => { },
            canExecute: () => allowed);

        Assert.False(cmd.IsExecutable);
        allowed = true;
        Assert.True(cmd.IsExecutable);
    }

    [Fact]
    public void CommandPaletteService_Registration_StoresAndRetrieves()
    {
        var service = new CommandPaletteService();
        var cmd1 = new PaletteCommand("hub.open", "Open Hub", "desc", "Productivity", () => { });
        var cmd2 = new PaletteCommand("tasks.open", "Open Tasks", "desc", "Productivity", () => { });

        service.Register(cmd1);
        service.Register(cmd2);

        Assert.Equal(2, service.Count);
        Assert.Equal(cmd1, service.GetCommand("hub.open"));
        Assert.Equal(cmd2, service.GetCommand("tasks.open"));
        Assert.Null(service.GetCommand("nonexistent"));

        var all = service.GetAllCommands();
        Assert.Equal(2, all.Count);
        Assert.Equal("hub.open", all[0].Id);
        Assert.Equal("tasks.open", all[1].Id);
    }

    [Fact]
    public void CommandPaletteService_RegisterDuplicateId_ReplacesWithoutDuplicate()
    {
        var service = new CommandPaletteService();
        var cmd1 = new PaletteCommand("cmd.1", "Version 1", "desc", "General", () => { });
        var cmd2 = new PaletteCommand("cmd.1", "Version 2", "desc updated", "General", () => { });

        service.Register(cmd1);
        service.Register(cmd2);

        Assert.Equal(1, service.Count);
        Assert.Equal("Version 2", service.GetCommand("cmd.1")?.Title);
        Assert.Single(service.GetAllCommands());
    }

    [Fact]
    public void CommandPaletteService_UnregisterAndClear_UpdatesState()
    {
        var service = new CommandPaletteService();
        var cmd1 = new PaletteCommand("c1", "Title 1", "d", "Cat", () => { });
        var cmd2 = new PaletteCommand("c2", "Title 2", "d", "Cat", () => { });

        service.RegisterRange(new[] { cmd1, cmd2 });
        Assert.Equal(2, service.Count);

        bool removed = service.Unregister("c1");
        Assert.True(removed);
        Assert.Equal(1, service.Count);
        Assert.Null(service.GetCommand("c1"));

        bool removeMissing = service.Unregister("nonexistent");
        Assert.False(removeMissing);

        service.Clear();
        Assert.Equal(0, service.Count);
        Assert.Empty(service.GetAllCommands());
    }

    [Fact]
    public void CommandPaletteService_Search_EmptyQuery_ReturnsAllInRegistrationOrder()
    {
        var service = new CommandPaletteService();
        var cmdA = new PaletteCommand("a", "Alpha", "d", "C", () => { });
        var cmdB = new PaletteCommand("b", "Beta", "d", "C", () => { });
        var cmdC = new PaletteCommand("c", "Gamma", "d", "C", () => { });

        service.RegisterRange(new[] { cmdA, cmdB, cmdC });

        var resultsEmpty = service.Search("");
        Assert.Equal(3, resultsEmpty.Count);
        Assert.Equal("Alpha", resultsEmpty[0].Title);
        Assert.Equal("Beta", resultsEmpty[1].Title);
        Assert.Equal("Gamma", resultsEmpty[2].Title);

        var resultsWhitespace = service.Search("   ");
        Assert.Equal(3, resultsWhitespace.Count);
    }

    [Fact]
    public void CommandPaletteService_Search_Ranking_ExactAndPrefixRankHigherThanFuzzy()
    {
        var service = new CommandPaletteService();
        var cmdExact = new PaletteCommand("focus.exact", "Focus", "desc", "Cat", () => { });
        var cmdPrefix = new PaletteCommand("focus.prefix", "Focus Timer Settings", "desc", "Cat", () => { });
        var cmdSubstring = new PaletteCommand("focus.sub", "Start Focus Session", "desc", "Cat", () => { });
        var cmdFuzzy = new PaletteCommand("focus.fuzzy", "Find Our Cute Unique Screen", "desc", "Cat", () => { });

        service.RegisterRange(new[] { cmdFuzzy, cmdSubstring, cmdPrefix, cmdExact });

        var results = service.Search("focus");
        Assert.NotEmpty(results);

        // Exact match must be first
        Assert.Equal("focus.exact", results[0].Id);
        // Prefix match must rank before substring match
        Assert.Equal("focus.prefix", results[1].Id);
        // Substring match must rank before fuzzy subsequence match
        Assert.Equal("focus.sub", results[2].Id);
        // Fuzzy match appears after exact/prefix/substring
        Assert.Contains(results, r => r.Id == "focus.fuzzy");
    }

    [Fact]
    public void CommandPaletteService_Search_AcronymAndWordBoundaryMatch()
    {
        var service = new CommandPaletteService();
        var cmdHub = new PaletteCommand("hub.open", "Open Productivity Hub", "desc", "Productivity", () => { });
        var cmdTasks = new PaletteCommand("tasks.create", "Create Task", "desc", "Productivity", () => { });

        service.RegisterRange(new[] { cmdHub, cmdTasks });

        // Acronym search "oph" -> Open Productivity Hub
        var ophResults = service.Search("oph");
        Assert.NotEmpty(ophResults);
        Assert.Equal("hub.open", ophResults[0].Id);

        // Word start search "task" -> Create Task
        var taskResults = service.Search("task");
        Assert.NotEmpty(taskResults);
        Assert.Equal("tasks.create", taskResults[0].Id);
    }

    [Fact]
    public void CommandPaletteService_Search_KeywordsAndCategoryMatch()
    {
        var service = new CommandPaletteService();
        var cmdFocus = new PaletteCommand(
            "focus.start", "Start Session", "Focus work block", "Productivity",
            () => { }, keywords: new[] { "pomodoro", "timer" });

        var cmdNotes = new PaletteCommand(
            "notes.open", "Open Quick Notes", "Scratchpad", "Notes",
            () => { });

        service.RegisterRange(new[] { cmdFocus, cmdNotes });

        // Keyword search "pomodoro"
        var pomoResults = service.Search("pomodoro");
        Assert.Single(pomoResults);
        Assert.Equal("focus.start", pomoResults[0].Id);

        // Category search "Notes"
        var catResults = service.Search("Notes");
        Assert.NotEmpty(catResults);
        Assert.Equal("notes.open", catResults[0].Id);
    }

    [Fact]
    public void CommandPaletteService_Search_NoMatch_ReturnsEmptyList()
    {
        var service = new CommandPaletteService();
        service.Register(new PaletteCommand("c1", "Open Hub", "desc", "Prod", () => { }));

        var results = service.Search("xyz999unrelated");
        Assert.Empty(results);
    }

    [Fact]
    public void CommandPaletteService_Execution_InvokesActionAndFiresEvent()
    {
        var service = new CommandPaletteService();
        bool actionRan = false;
        PaletteCommand? executedCmd = null;

        service.CommandExecuted += cmd => executedCmd = cmd;

        var cmd = new PaletteCommand("c1", "Action Test", "desc", "Cat", () => actionRan = true);
        service.Register(cmd);

        bool success = service.Execute("c1");
        Assert.True(success);
        Assert.True(actionRan);
        Assert.Equal(cmd, executedCmd);

        // Non-existent command returns false
        Assert.False(service.Execute("unknown"));
    }

    [Fact]
    public void CommandPaletteService_Execution_RespectsCanExecute()
    {
        var service = new CommandPaletteService();
        bool actionRan = false;
        var cmd = new PaletteCommand("c.disabled", "Disabled", "desc", "Cat", () => actionRan = true, canExecute: () => false);
        service.Register(cmd);

        bool success = service.Execute("c.disabled");
        Assert.False(success);
        Assert.False(actionRan);
    }

    [Fact]
    public void CommandPalette_RequiredCategoriesAndCoreActions_CanBeRegistered()
    {
        var service = new CommandPaletteService();

        // Register core productivity, notes, companion, application catalog
        service.Register(new PaletteCommand("hub.open", "Open Productivity Hub", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("tasks.open", "Open Tasks", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("tasks.create", "Create Task", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("focus.start", "Start Focus Session", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("focus.pause_resume", "Pause or Resume Focus", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("focus.cancel", "Cancel Focus Session", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("focus.short_break", "Start Short Break", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("focus.long_break", "Start Long Break", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("hub.history", "Open Focus History", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("hub.insights", "Open Insights & Analytics", "", "Productivity", () => { }));
        service.Register(new PaletteCommand("hub.goals", "Open Productivity Goals", "", "Productivity", () => { }));

        service.Register(new PaletteCommand("notes.open", "Open Quick Notes", "", "Notes", () => { }));
        service.Register(new PaletteCommand("notes.create", "Create New Note", "", "Notes", () => { }));

        service.Register(new PaletteCommand("companions.studio", "Open Companion Studio", "", "Companions", () => { }));
        service.Register(new PaletteCommand("companion.switch.billu", "Switch Companion: Billu", "", "Companions", () => { }));
        service.Register(new PaletteCommand("companion.switch.neku", "Switch Companion: Neku", "", "Companions", () => { }));

        service.Register(new PaletteCommand("app.summon", "Show or Summon Neku", "", "Application", () => { }));
        service.Register(new PaletteCommand("app.hide", "Hide Neku", "", "Application", () => { }));
        service.Register(new PaletteCommand("app.settings", "Open Settings", "", "Application", () => { }));
        service.Register(new PaletteCommand("app.quiet_mode", "Toggle Quiet Mode", "", "Application", () => { }));

        var categories = service.GetAllCommands().Select(c => c.Category).Distinct().ToList();
        Assert.Contains("Productivity", categories);
        Assert.Contains("Notes", categories);
        Assert.Contains("Companions", categories);
        Assert.Contains("Application", categories);

        Assert.NotNull(service.GetCommand("companion.switch.billu"));
        Assert.NotNull(service.GetCommand("companion.switch.neku"));
        Assert.NotNull(service.GetCommand("tasks.create"));
        Assert.NotNull(service.GetCommand("notes.create"));
    }

    [Fact]
    public void CommandPaletteWindow_CanRenderOffscreen_WithoutErrors()
    {
        var thread = new Thread(() =>
        {
            var service = new CommandPaletteService();
            service.Register(new PaletteCommand("hub.open", "Open Productivity Hub", "View productivity dashboard, tasks, focus sessions, and analytics", "Productivity", () => { }, "🐾"));
            service.Register(new PaletteCommand("tasks.create", "Create Task", "Quickly create a new task in your task list", "Productivity", () => { }, "➕", "Ctrl+N"));
            service.Register(new PaletteCommand("companion.switch.billu", "Switch Companion: Billu", "Loyal black & white indie street cat", "Companions", () => { }, "🐱"));
            service.Register(new PaletteCommand("app.settings", "Open Settings", "Configure pet scale, speed, and behavior", "Application", () => { }, "⚙️"));

            var window = new DesktopPet.UI.CommandPaletteWindow(service);
            var border = (System.Windows.FrameworkElement)window.Content;
            border.Measure(new System.Windows.Size(620, 420));
            border.Arrange(new System.Windows.Rect(0, 0, 620, 420));
            border.UpdateLayout();

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(620, 420, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(border);

            string scratchDir = @"C:\Users\amvbl\.gemini\antigravity\brain\c3f88a8b-28be-4879-94fd-b66f4fb7d2f1\scratch";
            if (System.IO.Directory.Exists(scratchDir))
            {
                string pngPath = System.IO.Path.Combine(scratchDir, "palette_preview.png");
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(pngPath);
                encoder.Save(fs);
            }

            // Test empty state transition and rendering
            window.SearchInputBox.Text = "nonexistent_query";
            border.UpdateLayout();
            Assert.Equal(System.Windows.Visibility.Visible, window.EmptyStatePanel.Visibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, window.ResultsListBox.Visibility);

            if (System.IO.Directory.Exists(scratchDir))
            {
                var rtbEmpty = new System.Windows.Media.Imaging.RenderTargetBitmap(620, 420, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtbEmpty.Render(border);
                string emptyPngPath = System.IO.Path.Combine(scratchDir, "palette_empty_preview.png");
                var encoderEmpty = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoderEmpty.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbEmpty));
                using var fsEmpty = System.IO.File.Create(emptyPngPath);
                encoderEmpty.Save(fsEmpty);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);
        Assert.True(finished);
    }

    [Fact]
    public void CommandPalette_ShortcutConfiguration_CanBeCustomizedAndValidated()
    {
        var settings = new AppSettings();
        // 1. Verify default is Ctrl+Alt+P
        Assert.True(settings.EnableCommandPaletteHotkey);
        Assert.Equal((int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), settings.CommandPaletteHotkeyModifiers);
        Assert.Equal((int)System.Windows.Input.Key.P, settings.CommandPaletteHotkeyKey);

        // 2. Customize to a new key combination (e.g. Ctrl + Alt + K)
        settings.CommandPaletteHotkeyModifiers = (int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt);
        settings.CommandPaletteHotkeyKey = (int)System.Windows.Input.Key.K;
        settings.Validate();

        Assert.Equal((int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), settings.CommandPaletteHotkeyModifiers);
        Assert.Equal((int)System.Windows.Input.Key.K, settings.CommandPaletteHotkeyKey);

        // 3. Corrupt/negative value falls back to safe default
        settings.CommandPaletteHotkeyModifiers = 0;
        settings.CommandPaletteHotkeyKey = -1;
        settings.Validate();

        Assert.Equal((int)(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), settings.CommandPaletteHotkeyModifiers);
        Assert.Equal((int)System.Windows.Input.Key.P, settings.CommandPaletteHotkeyKey);
    }

    [Fact]
    public void CommandPalette_ActionsEndToEnd_TaskCreation()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tasks_palette_test_{Guid.NewGuid():N}.json");
        try
        {
            var taskService = new TaskService(tempFile);
            var service = new CommandPaletteService();

            bool taskCreated = false;
            service.Register(new PaletteCommand(
                "tasks.create", "Create Task", "Add task", "Productivity",
                () =>
                {
                    taskService.Create("Palette Created Task", "Details", TaskPriority.Normal, null, 25);
                    taskCreated = true;
                },
                "➕", "Ctrl+N"));

            bool executed = service.Execute("tasks.create");
            Assert.True(executed);
            Assert.True(taskCreated);
            Assert.Single(taskService.Tasks);
            Assert.Equal("Palette Created Task", taskService.Tasks[0].Title);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void CommandPalette_ActionsEndToEnd_NoteCreation()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"notes_palette_test_{Guid.NewGuid():N}.json");
        try
        {
            var notesService = new NotesService(tempFile);
            var service = new CommandPaletteService();

            bool noteCreated = false;
            service.Register(new PaletteCommand(
                "notes.create", "Create Note", "Add note", "Notes",
                () =>
                {
                    notesService.Create("Palette Note", "Content from palette");
                    noteCreated = true;
                },
                "✏️"));

            bool executed = service.Execute("notes.create");
            Assert.True(executed);
            Assert.True(noteCreated);
            Assert.Single(notesService.Notes);
            Assert.Equal("Palette Note", notesService.Notes[0].Title);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void CommandPalette_ActionsEndToEnd_FocusTimerControls()
    {
        using var timer = new FocusTimerService();
        var service = new CommandPaletteService();

        service.Register(new PaletteCommand("focus.start", "Start Focus", "", "Productivity", () => timer.StartFocus(25)));
        service.Register(new PaletteCommand("focus.pause_resume", "Pause/Resume", "", "Productivity", () =>
        {
            if (timer.State == FocusTimerState.Focusing) timer.Pause();
            else if (timer.State == FocusTimerState.FocusPaused) timer.Resume();
        }));
        service.Register(new PaletteCommand("focus.short_break", "Short Break", "", "Productivity", () => timer.StartBreak(false, 5)));
        service.Register(new PaletteCommand("focus.cancel", "Cancel Focus", "", "Productivity", () => timer.Cancel()));

        // Start
        Assert.Equal(FocusTimerState.Idle, timer.State);
        service.Execute("focus.start");
        Assert.Equal(FocusTimerState.Focusing, timer.State);

        // Pause
        service.Execute("focus.pause_resume");
        Assert.Equal(FocusTimerState.FocusPaused, timer.State);

        // Resume
        service.Execute("focus.pause_resume");
        Assert.Equal(FocusTimerState.Focusing, timer.State);

        // Cancel
        service.Execute("focus.cancel");
        Assert.Equal(FocusTimerState.Idle, timer.State);

        // Short Break
        service.Execute("focus.short_break");
        Assert.Equal(FocusTimerState.Break, timer.State);
    }

    [Fact]
    public void CommandPalette_CompanionSwitching_RegistersInstalledPacksIncludingBillu()
    {
        var service = new CommandPaletteService();
        var packs = AssetSource.DiscoverPacks();

        foreach (var pack in packs)
        {
            if (!pack.IsValid) continue;
            if (pack.Id.Equals("neku-personal-local", StringComparison.OrdinalIgnoreCase)) continue;

            string packId = pack.Id;
            service.Register(new PaletteCommand(
                $"companion.switch.{packId}",
                $"Switch Companion: {pack.DisplayName}",
                pack.Description ?? "",
                "Companions",
                () => { },
                "🐱"));
        }

        // Billu must be registered
        var billuCmd = service.GetCommand("companion.switch.billu");
        Assert.NotNull(billuCmd);
        Assert.Equal("Companions", billuCmd.Category);
        Assert.Contains("Billu", billuCmd.Title);

        // Neku must be registered
        var nekuCmd = service.GetCommand("companion.switch.neku");
        Assert.NotNull(nekuCmd);
        Assert.Equal("Companions", nekuCmd.Category);

        // Private local pack must be excluded
        Assert.Null(service.GetCommand("companion.switch.neku-personal-local"));
    }
}
