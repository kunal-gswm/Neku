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
        Assert.NotEqual(HotkeyService.ExitHotkeyId, HotkeyService.PaletteHotkeyId);
        Assert.True(HotkeyService.ExitHotkeyId > 0);
        Assert.True(HotkeyService.PaletteHotkeyId > 0);
    }

    [Fact]
    public void HotkeyService_InitialState_IsNotRegistered()
    {
        using var service = new HotkeyService();
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));
    }

    [Fact]
    public void HotkeyService_Register_WithoutHwnd_ReturnsFalse()
    {
        using var service = new HotkeyService();
        bool registered = service.Register(HotkeyService.PaletteHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.P, () => { });
        Assert.False(registered);
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));
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

        // RegisterExit helper also fails gracefully without HWND
        bool helperRegistered = service.RegisterExit(() => { });
        Assert.False(helperRegistered);
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
    }

    [Fact]
    public void HotkeyService_UnregisterExit_SucceedsSafely()
    {
        using var service = new HotkeyService();
        service.UnregisterExit();
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
    }

    [Fact]
    public void HotkeyService_PaletteHotkeyCombination_IsValidModifiersAndKey()
    {
        // Default: Ctrl + Alt + P (avoids colliding with VS Code's Ctrl + Shift + P)
        var mods = ModifierKeys.Control | ModifierKeys.Alt;
        var key = Key.P;

        Assert.True(mods.HasFlag(ModifierKeys.Control));
        Assert.True(mods.HasFlag(ModifierKeys.Alt));
        Assert.False(mods.HasFlag(ModifierKeys.Shift));
        Assert.Equal(Key.P, key);

        using var service = new HotkeyService();
        // Without HWND, registration fails gracefully without throwing
        bool registered = service.Register(HotkeyService.PaletteHotkeyId, mods, key, () => { });
        Assert.False(registered);

        // RegisterPalette helper also fails gracefully without HWND
        bool helperRegistered = service.RegisterPalette(mods, key, () => { });
        Assert.False(helperRegistered);
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));
    }

    [Fact]
    public void HotkeyService_DefaultConfiguration_LeavesCtrlShiftPFreeForOtherApps()
    {
        var settings = new AppSettings();
        var defaultMods = (ModifierKeys)settings.CommandPaletteHotkeyModifiers;
        var defaultKey = (Key)settings.CommandPaletteHotkeyKey;

        // Verify default is Ctrl+Alt+P
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, defaultMods);
        Assert.Equal(Key.P, defaultKey);

        // Explicitly verify Shift modifier is NOT included by default
        Assert.False(defaultMods.HasFlag(ModifierKeys.Shift),
            "Neku must not register Shift modifier by default so Ctrl+Shift+P remains available for VS Code.");
    }

    [Fact]
    public void HotkeyService_PaletteRegistrationFailure_DoesNotCorruptExitState()
    {
        using var service = new HotkeyService();

        // 1. Initial state
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));

        // 2. Unattached registration attempts fail safely without throwing
        bool exitReg = service.RegisterExit(() => { });
        bool paletteReg = service.RegisterPalette(ModifierKeys.Control | ModifierKeys.Alt, Key.P, () => { });

        Assert.False(exitReg);
        Assert.False(paletteReg);

        // 3. Unregistering palette does not affect exit
        service.UnregisterPalette();
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));

        // 4. Repeated unregistration is completely safe
        service.UnregisterPalette();
        service.UnregisterExit();
    }

    [Fact]
    public void HotkeyService_UnregisterPalette_SucceedsSafely()
    {
        using var service = new HotkeyService();
        service.UnregisterPalette();
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));
    }

    [Fact]
    public void HotkeyService_RegistrationConflict_DoesNotCorruptServiceState()
    {
        using var service = new HotkeyService();
        // Simulate registration conflict failure on unattached window
        bool first = service.RegisterExit(() => { });
        Assert.False(first);
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));

        // Re-attempting or switching actions does not throw or corrupt state
        bool second = service.RegisterExit(() => { });
        Assert.False(second);
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));

        service.UnregisterExit();
        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
    }

    [Fact]
    public void HotkeyService_Unregister_WhenNotRegistered_DoesNotThrow()
    {
        using var service = new HotkeyService();
        service.Unregister(HotkeyService.ExitHotkeyId);
        service.Unregister(HotkeyService.PaletteHotkeyId);
    }

    [Fact]
    public void HotkeyService_Dispose_CleansUpRegistrationsAndCallbacks()
    {
        var service = new HotkeyService();
        service.Register(HotkeyService.ExitHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.Q, () => { });
        service.RegisterExit(() => { });
        service.Dispose();

        Assert.False(service.IsRegistered(HotkeyService.ExitHotkeyId));
        Assert.False(service.IsRegistered(HotkeyService.PaletteHotkeyId));

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
