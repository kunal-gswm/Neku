using System;
using System.Collections.Generic;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.DeskSense;
using Xunit;

namespace DesktopPet.Tests;

public class TestDeskSenseSignalProvider : IDeskSenseSignalProvider
{
    public string ProcessName { get; set; } = "explorer";
    public string WindowTitle { get; set; } = "Windows Explorer";
    public double IdleSeconds { get; set; } = 0.0;
    public bool IsQuiet { get; set; } = false;
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public (string ProcessName, string WindowTitle) GetForegroundApp() => (ProcessName, WindowTitle);
    public double GetIdleSeconds() => IdleSeconds;
    public bool IsPresentationOrQuietMode() => IsQuiet;
    public DateTimeOffset GetUtcNow() => Now;
}

public class DeskSenseTests
{
    [Theory]
    [InlineData("code", DeskSenseAppCategory.Coding)]
    [InlineData("devenv", DeskSenseAppCategory.Coding)]
    [InlineData("idea64", DeskSenseAppCategory.Coding)]
    [InlineData("studio64", DeskSenseAppCategory.Coding)]
    [InlineData("cursor", DeskSenseAppCategory.Coding)]
    [InlineData("windowsterminal", DeskSenseAppCategory.Terminal)]
    [InlineData("powershell", DeskSenseAppCategory.Terminal)]
    [InlineData("pwsh", DeskSenseAppCategory.Terminal)]
    [InlineData("cmd", DeskSenseAppCategory.Terminal)]
    [InlineData("chrome", DeskSenseAppCategory.Browser)]
    [InlineData("firefox", DeskSenseAppCategory.Browser)]
    [InlineData("msedge", DeskSenseAppCategory.Browser)]
    [InlineData("teams", DeskSenseAppCategory.Meeting)]
    [InlineData("zoom", DeskSenseAppCategory.Meeting)]
    [InlineData("slack", DeskSenseAppCategory.Meeting)]
    [InlineData("notepad", DeskSenseAppCategory.Other)]
    public void CategorizeProcess_CategorizesKnownApplicationsCorrectly(string processName, DeskSenseAppCategory expectedCategory)
    {
        var category = DeskSenseService.CategorizeProcess(processName);
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void PollSignals_DebouncesTransientProcessChanges()
    {
        var settings = new AppSettings
        {
            EnableDeskSense = true,
            EnableForegroundAppDetection = true
        };
        var signals = new TestDeskSenseSignalProvider { ProcessName = "explorer" };
        var service = new DeskSenseService(settings, signals, dispatcher: null);

        var transitions = new List<DeskSenseTransition>();
        service.TransitionOccurred += (t, _) => transitions.Add(t);

        // First sample of "explorer"
        service.Poll();
        Assert.Empty(transitions);

        // Single transient flicker to "code"
        signals.ProcessName = "code";
        service.Poll();
        Assert.DoesNotContain(DeskSenseTransition.CodingStarted, transitions);

        // Switch back to "explorer" before debounce confirmation
        signals.ProcessName = "explorer";
        service.Poll();
        Assert.DoesNotContain(DeskSenseTransition.CodingStarted, transitions);
    }

    [Fact]
    public void PollSignals_TriggersCodingStarted_WhenStable()
    {
        var settings = new AppSettings
        {
            EnableDeskSense = true,
            EnableForegroundAppDetection = true
        };
        var signals = new TestDeskSenseSignalProvider { ProcessName = "explorer" };
        var service = new DeskSenseService(settings, signals, dispatcher: null);

        var transitions = new List<DeskSenseTransition>();
        service.TransitionOccurred += (t, _) => transitions.Add(t);

        // Baseline
        service.Poll();
        service.Poll();

        // Switch to "code" and maintain for 2 samples
        signals.ProcessName = "code";
        service.Poll();
        service.Poll();

        Assert.Contains(DeskSenseTransition.CodingStarted, transitions);
        Assert.Equal(DeskSenseAppCategory.Coding, service.CurrentContext.Category);
        Assert.Equal("code", service.CurrentContext.ProcessName);
    }

    [Fact]
    public void PollSignals_DetectsUserIdleAndReturnedTransitions()
    {
        var settings = new AppSettings
        {
            EnableDeskSense = true,
            EnableIdleDetection = true,
            IdleThresholdSeconds = 60
        };
        var signals = new TestDeskSenseSignalProvider
        {
            ProcessName = "code",
            IdleSeconds = 10
        };
        var service = new DeskSenseService(settings, signals, dispatcher: null);

        var transitions = new List<DeskSenseTransition>();
        service.TransitionOccurred += (t, _) => transitions.Add(t);

        // Active state
        service.Poll();
        Assert.False(service.CurrentContext.IsIdle);

        // Transition to idle (exceeds 60s threshold)
        signals.IdleSeconds = 65;
        service.Poll();

        Assert.True(service.CurrentContext.IsIdle);
        Assert.Contains(DeskSenseTransition.UserIdle, transitions);

        // User returned (input activity resets idle seconds to 0)
        signals.IdleSeconds = 0;
        service.Poll();

        Assert.False(service.CurrentContext.IsIdle);
        Assert.Contains(DeskSenseTransition.UserReturned, transitions);
    }

    [Fact]
    public void PollSignals_DetectsPresentationMode()
    {
        var settings = new AppSettings
        {
            EnableDeskSense = true
        };
        var signals = new TestDeskSenseSignalProvider
        {
            ProcessName = "powerpnt",
            IsQuiet = false
        };
        var service = new DeskSenseService(settings, signals, dispatcher: null);

        var transitions = new List<DeskSenseTransition>();
        service.TransitionOccurred += (t, _) => transitions.Add(t);

        service.Poll();
        Assert.False(service.CurrentContext.IsPresentingOrQuiet);

        // Presentation starts
        signals.IsQuiet = true;
        service.Poll();

        Assert.True(service.CurrentContext.IsPresentingOrQuiet);
        Assert.Contains(DeskSenseTransition.PresentationStarted, transitions);
    }

    [Fact]
    public void Personality_GeneratesDistinctDeskSenseLines_ForNekuAndBillu()
    {
        var nekuPersonality = new Personality();
        nekuPersonality.SetActivePet("cat-orange");
        var billuPersonality = new Personality();
        billuPersonality.SetActivePet("billu");

        // Coding lines
        string nekuCoding = nekuPersonality.GetCodingStartedLine();
        string billuCoding = billuPersonality.GetCodingStartedLine();
        Assert.False(string.IsNullOrWhiteSpace(nekuCoding));
        Assert.False(string.IsNullOrWhiteSpace(billuCoding));

        // Terminal lines
        string nekuTerminal = nekuPersonality.GetTerminalActiveLine();
        string billuTerminal = billuPersonality.GetTerminalActiveLine();
        Assert.False(string.IsNullOrWhiteSpace(nekuTerminal));
        Assert.False(string.IsNullOrWhiteSpace(billuTerminal));

        // Idle lines
        string nekuIdle = nekuPersonality.GetUserIdleLine();
        string billuIdle = billuPersonality.GetUserIdleLine();
        Assert.False(string.IsNullOrWhiteSpace(nekuIdle));
        Assert.False(string.IsNullOrWhiteSpace(billuIdle));

        // Returned lines
        string nekuReturned = nekuPersonality.GetUserReturnedLine();
        string billuReturned = billuPersonality.GetUserReturnedLine();
        Assert.False(string.IsNullOrWhiteSpace(nekuReturned));
        Assert.False(string.IsNullOrWhiteSpace(billuReturned));

        // Presentation lines
        string nekuPres = nekuPersonality.GetPresentationStartedLine();
        string billuPres = billuPersonality.GetPresentationStartedLine();
        Assert.False(string.IsNullOrWhiteSpace(nekuPres));
        Assert.False(string.IsNullOrWhiteSpace(billuPres));
    }
}
