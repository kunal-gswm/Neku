using System;

namespace DesktopPet.Services.DeskSense;

/// <summary>
/// Broad category of the active foreground application.
/// </summary>
public enum DeskSenseAppCategory
{
    Other,
    Coding,
    Terminal,
    Browser,
    Meeting,
    Idle
}

/// <summary>
/// Meaningful high-level desktop context transitions detected by DeskSense.
/// </summary>
public enum DeskSenseTransition
{
    None,
    CodingStarted,
    CodingPaused,
    TerminalActive,
    PresentationStarted,
    UserIdle,
    UserReturned,
    DevelopmentActivityChanged
}

/// <summary>
/// Snapshot of the current local desktop context.
/// </summary>
public sealed class DeskSenseContext
{
    public string ProcessName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public DeskSenseAppCategory Category { get; init; } = DeskSenseAppCategory.Other;
    public bool IsIdle { get; init; }
    public double IdleSeconds { get; init; }
    public bool IsPresentingOrQuiet { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsPresentationOrMeeting => IsPresentingOrQuiet || Category == DeskSenseAppCategory.Meeting;
    public string DurationDescription => IsIdle ? $"Idle for {IdleSeconds:F0}s" : "Active";

    public override string ToString() =>
        $"[{Category}] {ProcessName} (Idle: {IsIdle}, {IdleSeconds:F0}s, Quiet: {IsPresentingOrQuiet})";
}

/// <summary>
/// Abstraction for system signal retrieval to allow deterministic unit testing.
/// </summary>
public interface IDeskSenseSignalProvider
{
    (string ProcessName, string WindowTitle) GetForegroundApp();
    double GetIdleSeconds();
    bool IsPresentationOrQuietMode();
    DateTimeOffset GetUtcNow();
}
