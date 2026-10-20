using System.Windows;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class DeveloperMenuVisibilityTests
{
    [Fact]
    public void TrayService_WhenAllDeveloperOptionsDisabled_HidesDeveloperSubmenu()
    {
        var settings = new AppSettings
        {
            EnableDeveloperEvents = false,
            EnableDeveloperRadar = false,
            EnableDeveloperHud = false
        };

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
            onDeveloperRadar: () => { },
            onToggleDeveloperHud: () => { });

        Assert.False(tray.IsDevSubmenuVisible);
        Assert.False(tray.IsDevEventsVisible);
        Assert.False(tray.IsRadarVisible);
        Assert.False(tray.IsHudVisible);
    }

    [Fact]
    public void TrayService_WhenDeveloperEventsEnabled_ShowsSubmenuAndEventsOnly()
    {
        var settings = new AppSettings
        {
            EnableDeveloperEvents = true,
            EnableDeveloperRadar = false,
            EnableDeveloperHud = false
        };

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
            onDeveloperRadar: () => { },
            onToggleDeveloperHud: () => { });

        Assert.True(tray.IsDevSubmenuVisible);
        Assert.True(tray.IsDevEventsVisible);
        Assert.False(tray.IsRadarVisible);
        Assert.False(tray.IsHudVisible);
    }

    [Fact]
    public void TrayService_WhenRadarEnabled_ShowsSubmenuAndRadar()
    {
        var settings = new AppSettings
        {
            EnableDeveloperEvents = false,
            EnableDeveloperRadar = true,
            EnableDeveloperHud = false
        };

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
            onDeveloperRadar: () => { },
            onToggleDeveloperHud: () => { });

        Assert.True(tray.IsDevSubmenuVisible);
        Assert.False(tray.IsDevEventsVisible);
        Assert.True(tray.IsRadarVisible);
        Assert.True(tray.IsHudVisible);

        // Dynamically turn off radar
        settings.EnableDeveloperRadar = false;
        tray.RefreshDeveloperMenuVisibility();

        Assert.False(tray.IsDevSubmenuVisible);
        Assert.False(tray.IsRadarVisible);
    }
}
