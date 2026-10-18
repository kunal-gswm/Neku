using System.IO;
using System.Windows.Input;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void HotkeyConstants_AreDistinct()
    {
        Assert.NotEqual(HotkeyService.ChatHotkeyId, HotkeyService.ExitHotkeyId);
        Assert.True(HotkeyService.ChatHotkeyId > 0);
        Assert.True(HotkeyService.ExitHotkeyId > 0);
    }

    [Fact]
    public void HotkeyService_InitialState_IsNotRegistered()
    {
        using var service = new HotkeyService();
        Assert.False(service.IsRegistered(HotkeyService.ChatHotkeyId));
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
    }

    [Fact]
    public void HotkeyService_Register_WithoutHwnd_ReturnsFalse()
    {
        using var service = new HotkeyService();
        bool registered = service.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.N);
        Assert.False(registered);
        Assert.False(service.IsRegistered(HotkeyService.ChatHotkeyId));
    }

    [Fact]
    public void HotkeyService_Register_WithKeyNone_ReturnsFalse()
    {
        using var service = new HotkeyService();
        bool registered = service.Register(HotkeyService.ExitHotkeyId, ModifierKeys.Control, Key.None, () => { });
        Assert.False(registered);
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
    }

    [Fact]
    public void HotkeyService_ExitHotkeyCombination_IsValidModifiersAndKey()
    {
        // Ctrl + Alt + Q definition
        var mods = ModifierKeys.Control | ModifierKeys.Alt;
        var key = Key.Q;

        Assert.True(mods.HasFlag(ModifierKeys.Control));
        Assert.True(mods.HasFlag(ModifierKeys.Alt));
        Assert.False(mods.HasFlag(ModifierKeys.Shift));
        Assert.Equal(Key.Q, key);

        using var service = new HotkeyService();
        // Without HWND, registration fails gracefully without throwing
        bool registered = service.Register(HotkeyService.ExitHotkeyId, mods, key, () => { });
        Assert.False(registered);
    }

    [Fact]
    public void HotkeyService_Unregister_WhenNotRegistered_DoesNotThrow()
    {
        using var service = new HotkeyService();
        service.Unregister(HotkeyService.ExitHotkeyId);
        service.Unregister();
    }

    [Fact]
    public void HotkeyService_Dispose_CleansUpRegistrationsAndCallbacks()
    {
        var service = new HotkeyService();
        service.Register(HotkeyService.ExitHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.Q, () => { });
        service.Dispose();

        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
        Assert.False(service.IsRegistered(HotkeyService.ChatHotkeyId));

        // Multiple disposals should remain completely safe
        service.Dispose();
    }

    [Fact]
    public void CleanExit_PersistsPendingNotesAndFocusState()
    {
        var settings = new AppSettings
        {
            RememberPetState = true,
            LastPetX = 100,
            LastPetY = 200
        };

        // 1. Verify FocusTimer state persistence
        using var focusTimer = new FocusTimerService();
        focusTimer.StartFocus(25);
        Assert.True(focusTimer.IsActive);

        focusTimer.SaveState(settings);
        Assert.True(settings.FocusTimerActive);
        Assert.NotNull(settings.FocusTimerDeadlineUtc);

        // 2. Verify NotesService persistence
        string tempFile = Path.Combine(Path.GetTempPath(), $"neku_notes_test_{Guid.NewGuid():N}.json");
        try
        {
            var notesService = new NotesService(tempFile);
            notesService.Create("Exit Test Note", "Testing clean exit serialization.");
            notesService.Save();

            Assert.True(File.Exists(tempFile));
            string json = File.ReadAllText(tempFile);
            Assert.Contains("Exit Test Note", json);
            Assert.Contains("Testing clean exit serialization.", json);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
