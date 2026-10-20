using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public sealed class AppSettings
{
    [JsonPropertyName("speed")] public double Speed { get; set; } = 1.0;
    [JsonPropertyName("enableWindowWalking")] public bool EnableWindowWalking { get; set; } = true;
    [JsonPropertyName("enableCursorChase")] public bool EnableCursorChase { get; set; } = true;
    [JsonPropertyName("enableSleep")] public bool EnableSleep { get; set; } = true;
    [JsonPropertyName("activePet")] public string ActivePet { get; set; } = "neku";
    [JsonPropertyName("autostart")] public bool Autostart { get; set; } = false;

    /// <summary>0 = off. Valid values: 15, 30, 45, 60 minutes.</summary>
    [JsonPropertyName("stretchIntervalMinutes")] public int StretchIntervalMinutes { get; set; } = 30;

    /// <summary>Cat unrolls toilet paper when you scroll the mouse wheel.</summary>
    [JsonPropertyName("enableScrollPlay")] public bool EnableScrollPlay { get; set; } = true;

    /// <summary>Overall size multiplier for the character (1.0 = default). Range ~0.5–2.0.</summary>
    [JsonPropertyName("sizeScale")] public double SizeScale { get; set; } = 1.0;

    /// <summary>Window opacity between 0.2 and 1.0.</summary>
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1.0;

    /// <summary>Enable autonomous wandering across the screen.</summary>
    [JsonPropertyName("enableAutonomousWandering")] public bool EnableAutonomousWandering { get; set; } = true;

    /// <summary>Enable random expressive reactions and remarks.</summary>
    [JsonPropertyName("enableRandomReactions")] public bool EnableRandomReactions { get; set; } = true;

    /// <summary>Disable all optional background monitors (keyboard/mouse/system) for zero overhead.</summary>
    [JsonPropertyName("disableOptionalMonitoring")] public bool DisableOptionalMonitoring { get; set; } = false;

    /// <summary>Lifelike moods: time-of-day rhythm plus energy/hunger drive behaviour.</summary>
    [JsonPropertyName("enableMoods")] public bool EnableMoods { get; set; } = true;

    /// <summary>React to the computer: naps when you're away, livelier under load,
    /// calmer in focus apps, rests more on low battery.</summary>
    [JsonPropertyName("enableSystemReactions")] public bool EnableSystemReactions { get; set; } = true;

    /// <summary>Check GitHub for a newer build on startup and offer a one-click update.</summary>
    [JsonPropertyName("enableAutoUpdate")] public bool EnableAutoUpdate { get; set; } = true;

    /// <summary>Settle down while you present, screen-share, or play a full-screen game.</summary>
    [JsonPropertyName("enableQuietMode")] public bool EnableQuietMode { get; set; } = true;

    /// <summary>Hide the cat entirely during those moments instead of just calming it down.</summary>
    [JsonPropertyName("hideWhenPresenting")] public bool HideWhenPresenting { get; set; } = false;

    /// <summary>
    /// Simulation ticks per second; 0 follows the compositor. Not exposed in Settings, because
    /// measurement showed it saves nothing: the CPU cost of the pet is dominated by being
    /// subscribed to WPF's render loop at all (~2.9% of a core with an empty handler), not by
    /// the work done per tick. It is kept as a cap for very high refresh-rate displays, where
    /// the simulation would otherwise run several times more often than anything can show.
    /// </summary>
    [JsonPropertyName("targetFps")] public int TargetFps { get; set; } = 60;
    [JsonPropertyName("useSoftwareRendering")] public bool UseSoftwareRendering { get; set; } = false;

    // ── Continuity: remember where the cat was and how it felt ──────────────────

    /// <summary>Pick up where the cat left off (position and mood) instead of resetting.</summary>
    [JsonPropertyName("rememberPetState")] public bool RememberPetState { get; set; } = true;

    /// <summary>Last known position in DIPs. NaN = never saved.</summary>
    [JsonPropertyName("lastPetX")] public double LastPetX { get; set; } = double.NaN;
    [JsonPropertyName("lastPetY")] public double LastPetY { get; set; } = double.NaN;

    /// <summary>Last known drives, 0..1. Negative = never saved.</summary>
    [JsonPropertyName("lastEnergy")] public double LastEnergy { get; set; } = -1;
    [JsonPropertyName("lastHunger")] public double LastHunger { get; set; } = -1;

    // ── Neku Personality & Interactions ───────────────────────────────────────
    [JsonPropertyName("personalityMood")] public double PersonalityMood { get; set; } = 0.6;
    [JsonPropertyName("personalityEnergy")] public double PersonalityEnergy { get; set; } = 0.7;
    [JsonPropertyName("personalityFamiliarity")] public double PersonalityFamiliarity { get; set; } = 0.1;
    [JsonPropertyName("personalityMischief")] public double PersonalityMischief { get; set; } = 0.3;
    [JsonPropertyName("totalPets")] public int TotalPets { get; set; } = 0;
    [JsonPropertyName("totalBoops")] public int TotalBoops { get; set; } = 0;
    [JsonPropertyName("totalDrags")] public int TotalDrags { get; set; } = 0;
    [JsonPropertyName("reactionsUnlocked")] public int ReactionsUnlocked { get; set; } = 0;

    // ── Accessibility & Performance ───────────────────────────────────────────
    /// <summary>Click-through mode allows mouse events to pass directly through the pet window.</summary>
    [JsonPropertyName("clickThrough")] public bool ClickThrough { get; set; } = false;

    /// <summary>Reduced motion limits fast erratic animations (e.g. zoomies, spins).</summary>
    [JsonPropertyName("reducedMotion")] public bool ReducedMotion { get; set; } = false;

    /// <summary>Battery-conscious mode throttles animations and background work when running on battery.</summary>
    [JsonPropertyName("batteryConscious")] public bool BatteryConscious { get; set; } = true;

    // ── Developer Personality Integration (Disabled by default) ───────────────
    /// <summary>Enable local developer event reactions (builds, tests, commands).</summary>
    [JsonPropertyName("enableDeveloperEvents")] public bool EnableDeveloperEvents { get; set; } = false;
    [JsonPropertyName("developerPort")] public int DeveloperPort { get; set; } = 48123;

    // ── Focus Timer (Pomodoro) ────────────────────────────────────────────────
    [JsonPropertyName("focusDurationMinutes")] public int FocusDurationMinutes { get; set; } = 25;
    [JsonPropertyName("breakDurationMinutes")] public int BreakDurationMinutes { get; set; } = 5;
    [JsonPropertyName("longBreakDurationMinutes")] public int LongBreakDurationMinutes { get; set; } = 15;
    [JsonPropertyName("longBreakInterval")] public int LongBreakInterval { get; set; } = 4;
    [JsonPropertyName("autoStartBreaks")] public bool AutoStartBreaks { get; set; } = false;
    [JsonPropertyName("autoStartFocus")] public bool AutoStartFocus { get; set; } = false;
    [JsonPropertyName("quietStart")] public bool QuietStart { get; set; } = false;
    [JsonPropertyName("focusTimerActive")] public bool FocusTimerActive { get; set; } = false;
    [JsonPropertyName("focusTimerState")] public string FocusTimerState { get; set; } = "Idle";
    [JsonPropertyName("focusTimerDeadlineUtc")] public DateTime? FocusTimerDeadlineUtc { get; set; }
    [JsonPropertyName("focusTimerRemainingSeconds")] public double FocusTimerRemainingSeconds { get; set; } = 0;
    [JsonPropertyName("focusActiveTaskId")] public string? FocusActiveTaskId { get; set; }
    [JsonPropertyName("focusTimerSessionType")] public string FocusTimerSessionType { get; set; } = "Focus";
    [JsonPropertyName("focusCycleCompletedSessions")] public int FocusCycleCompletedSessions { get; set; } = 0;

    // ── Productivity Goals ───────────────────────────────────────────────────
    [JsonPropertyName("dailyFocusGoalMinutes")] public int DailyFocusGoalMinutes { get; set; } = 100;
    [JsonPropertyName("weeklyFocusGoalMinutes")] public int WeeklyFocusGoalMinutes { get; set; } = 500;
    [JsonPropertyName("dailyTaskGoal")] public int DailyTaskGoal { get; set; } = 3;
    [JsonPropertyName("weeklyTaskGoal")] public int WeeklyTaskGoal { get; set; } = 15;

    // ── Productivity Reminders ───────────────────────────────────────────────
    [JsonPropertyName("enableTaskReminders")] public bool EnableTaskReminders { get; set; } = true;
    [JsonPropertyName("enableScheduledReminders")] public bool EnableScheduledReminders { get; set; } = true;
    [JsonPropertyName("defaultSnoozeMinutes")] public int DefaultSnoozeMinutes { get; set; } = 15;
    [JsonPropertyName("enableQuietHours")] public bool EnableQuietHours { get; set; } = false;
    [JsonPropertyName("quietHoursStart")] public string QuietHoursStart { get; set; } = "22:00";
    [JsonPropertyName("quietHoursEnd")] public string QuietHoursEnd { get; set; } = "08:00";
    [JsonPropertyName("enableUpcomingTaskReminders")] public bool EnableUpcomingTaskReminders { get; set; } = true;
    [JsonPropertyName("enableOverdueTaskReminders")] public bool EnableOverdueTaskReminders { get; set; } = true;
    [JsonPropertyName("enableBreakReminders")] public bool EnableBreakReminders { get; set; } = true;
    [JsonPropertyName("enableFocusCompletedReminders")] public bool EnableFocusCompletedReminders { get; set; } = true;
    [JsonPropertyName("enableBreakCompletedReminders")] public bool EnableBreakCompletedReminders { get; set; } = true;
    [JsonPropertyName("reminderIntervalMinutes")] public int ReminderIntervalMinutes { get; set; } = 30;

    // ── Quick Capture ─────────────────────────────────────────────────────────
    [JsonPropertyName("enableQuickCaptureHotkey")] public bool EnableQuickCaptureHotkey { get; set; } = true;
    [JsonPropertyName("quickCaptureHotkeyModifiers")] public int QuickCaptureHotkeyModifiers { get; set; } = 2 | 1;
    [JsonPropertyName("quickCaptureHotkeyKey")] public int QuickCaptureHotkeyKey { get; set; } = 46;

    // ── Onboarding ────────────────────────────────────────────────────────────
    /// <summary>Whether the first-run onboarding speech bubble has been dismissed.</summary>
    [JsonPropertyName("onboardingDismissed")] public bool OnboardingDismissed { get; set; } = false;

    // ── Companion Studio & Reaction Toggles ────────────────────────────────────
    /// <summary>Named personality preset: "Calm", "Playful", "Expressive", or "Custom".</summary>
    [JsonPropertyName("personalityPreset")] public string PersonalityPreset { get; set; } = "Expressive";

    /// <summary>Whether focus session start and completion trigger companion reactions.</summary>
    [JsonPropertyName("enableFocusReactions")] public bool EnableFocusReactions { get; set; } = true;

    /// <summary>Whether break session start and completion trigger companion reactions.</summary>
    [JsonPropertyName("enableBreakReactions")] public bool EnableBreakReactions { get; set; } = true;

    /// <summary>Whether petting and tapping spawn heart bursts and affection reactions.</summary>
    [JsonPropertyName("enablePettingReactions")] public bool EnablePettingReactions { get; set; } = true;

    /// <summary>Whether developer build success/failure triggers companion reactions.</summary>
    [JsonPropertyName("enableDevBuildReactions")] public bool EnableDevBuildReactions { get; set; } = true;

    /// <summary>Whether developer test pass/fail triggers companion reactions.</summary>
    [JsonPropertyName("enableDevTestReactions")] public bool EnableDevTestReactions { get; set; } = true;

    /// <summary>Whether task completions trigger companion reactions.</summary>
    [JsonPropertyName("enableTaskReactions")] public bool EnableTaskReactions { get; set; } = true;

    // ── Command Palette ───────────────────────────────────────────────────────
    /// <summary>Enable the global keyboard shortcut to toggle the Command Palette.</summary>
    [JsonPropertyName("enableCommandPaletteHotkey")] public bool EnableCommandPaletteHotkey { get; set; } = true;

    /// <summary>Hotkey modifiers as WPF ModifierKeys flags (Alt=1, Control=2, Shift=4, Windows=8). Default Ctrl+Alt (3).</summary>
    [JsonPropertyName("commandPaletteHotkeyModifiers")] public int CommandPaletteHotkeyModifiers { get; set; } = 2 | 1;

    /// <summary>Hotkey key as a WPF Key enum value. Default Key.P (59).</summary>
    [JsonPropertyName("commandPaletteHotkeyKey")] public int CommandPaletteHotkeyKey { get; set; } = 59;

    // ── AI Coding Agent Companion (Disabled by default) ───────────────────────
    /// <summary>Master toggle to enable AI coding agent monitoring and companion features.</summary>
    [JsonPropertyName("enableAgentCompanion")] public bool EnableAgentCompanion { get; set; } = false;

    /// <summary>Whether agent events trigger companion speech and animations.</summary>
    [JsonPropertyName("enableAgentReactions")] public bool EnableAgentReactions { get; set; } = true;

    /// <summary>Reaction when an agent session begins.</summary>
    [JsonPropertyName("enableAgentStartedReaction")] public bool EnableAgentStartedReaction { get; set; } = true;

    /// <summary>Reaction when an agent is waiting for input or terminal confirmation.</summary>
    [JsonPropertyName("enableAgentWaitingReaction")] public bool EnableAgentWaitingReaction { get; set; } = true;

    /// <summary>Reaction when an agent completes its turn or task.</summary>
    [JsonPropertyName("enableAgentCompletedReaction")] public bool EnableAgentCompletedReaction { get; set; } = true;

    /// <summary>Reaction when an agent turn or task fails.</summary>
    [JsonPropertyName("enableAgentFailedReaction")] public bool EnableAgentFailedReaction { get; set; } = true;

    /// <summary>Reaction when all tracked agent sessions become idle.</summary>
    [JsonPropertyName("enableAgentIdleReaction")] public bool EnableAgentIdleReaction { get; set; } = true;

    /// <summary>Max agent sessions retained in local history.</summary>
    [JsonPropertyName("agentMaxSessionsRetained")] public int AgentMaxSessionsRetained { get; set; } = 50;

    /// <summary>Max events per agent session retained in local history.</summary>
    [JsonPropertyName("agentMaxEventsPerSession")] public int AgentMaxEventsPerSession { get; set; } = 100;

    // ── DeskSense: Desktop Context Engine ─────────────────────────────────────
    /// <summary>Master toggle to enable DeskSense context engine.</summary>
    [JsonPropertyName("enableDeskSense")] public bool EnableDeskSense { get; set; } = true;

    /// <summary>Detect foreground application process and window identity.</summary>
    [JsonPropertyName("enableForegroundAppDetection")] public bool EnableForegroundAppDetection { get; set; } = true;

    /// <summary>Detect user idle state based on last input timestamp.</summary>
    [JsonPropertyName("enableIdleDetection")] public bool EnableIdleDetection { get; set; } = true;

    /// <summary>Inactivity threshold in seconds before declaring the user idle. Default 120s.</summary>
    [JsonPropertyName("idleThresholdSeconds")] public int IdleThresholdSeconds { get; set; } = 120;

    /// <summary>Master toggle for pet reactions to DeskSense context changes.</summary>
    [JsonPropertyName("enableDeskSenseReactions")] public bool EnableDeskSenseReactions { get; set; } = true;

    /// <summary>Reaction when coding begins in an IDE or editor.</summary>
    [JsonPropertyName("enableCodingStartedReaction")] public bool EnableCodingStartedReaction { get; set; } = true;

    /// <summary>Reaction when a terminal becomes active.</summary>
    [JsonPropertyName("enableTerminalActiveReaction")] public bool EnableTerminalActiveReaction { get; set; } = true;

    /// <summary>Reaction when user becomes idle.</summary>
    [JsonPropertyName("enableUserIdleReaction")] public bool EnableUserIdleReaction { get; set; } = true;

    /// <summary>Reaction when user returns from idle.</summary>
    [JsonPropertyName("enableUserReturnedReaction")] public bool EnableUserReturnedReaction { get; set; } = true;

    /// <summary>Reaction when presentation or screen sharing begins.</summary>
    [JsonPropertyName("enablePresentationStartedReaction")] public bool EnablePresentationStartedReaction { get; set; } = true;

    // ── Developer Radar & Floating HUD ────────────────────────────────────────
    /// <summary>Master toggle for Developer Radar tab in Productivity Hub.</summary>
    [JsonPropertyName("enableDeveloperRadar")] public bool EnableDeveloperRadar { get; set; } = true;

    /// <summary>Show compact floating Developer HUD overlay on desktop.</summary>
    [JsonPropertyName("enableDeveloperHud")] public bool EnableDeveloperHud { get; set; } = false;

    /// <summary>Last saved X coordinate of the floating Developer HUD.</summary>
    [JsonPropertyName("developerHudX")] public double DeveloperHudX { get; set; } = double.NaN;

    /// <summary>Last saved Y coordinate of the floating Developer HUD.</summary>
    [JsonPropertyName("developerHudY")] public double DeveloperHudY { get; set; } = double.NaN;

    /// <summary>List of explicitly monitored local Git repository paths.</summary>
    [JsonPropertyName("monitoredRepositoryPaths")] public List<string> MonitoredRepositoryPaths { get; set; } = new();

    /// <summary>Path of currently active/selected repository in Developer Radar.</summary>
    [JsonPropertyName("selectedRepositoryPath")] public string? SelectedRepositoryPath { get; set; }

    /// <summary>Whether background repository polling/watching is paused.</summary>
    [JsonPropertyName("pauseRepositoryMonitoring")] public bool PauseRepositoryMonitoring { get; set; } = false;

    // ── Desktop Pet Physics 2.0 & Toys ────────────────────────────────────────
    /// <summary>Smooth acceleration and deceleration for companion movement.</summary>
    [JsonPropertyName("enableSmoothMovement")] public bool EnableSmoothMovement { get; set; } = true;

    /// <summary>Screen-edge and corner resting behaviors (loafing/sitting at desk boundaries).</summary>
    [JsonPropertyName("enableEdgeResting")] public bool EnableEdgeResting { get; set; } = true;

    /// <summary>Allow companion to notice and interact with virtual toys on the desktop.</summary>
    [JsonPropertyName("enableToyInteractions")] public bool EnableToyInteractions { get; set; } = true;

    // ── Neku Dropzone ─────────────────────────────────────────────────────────
    /// <summary>Master toggle to accept dragged text, files, and links onto the pet.</summary>
    [JsonPropertyName("enableDropzone")] public bool EnableDropzone { get; set; } = true;

    /// <summary>Default action on drop: "Prompt", "Note", or "Task". Default "Prompt".</summary>
    [JsonPropertyName("dropzoneDefaultAction")] public string DropzoneDefaultAction { get; set; } = "Prompt";

    // ── System Health Companion ───────────────────────────────────────────────
    /// <summary>Master toggle for local system health monitoring (CPU, RAM, Disk, Battery).</summary>
    [JsonPropertyName("enableSystemHealthMonitoring")] public bool EnableSystemHealthMonitoring { get; set; } = true;

    /// <summary>Sustained CPU usage threshold percentage before alert triggers (default 85%).</summary>
    [JsonPropertyName("systemHealthCpuThreshold")] public int SystemHealthCpuThreshold { get; set; } = 85;

    /// <summary>Sustained RAM usage threshold percentage before alert triggers (default 90%).</summary>
    [JsonPropertyName("systemHealthRamThreshold")] public int SystemHealthRamThreshold { get; set; } = 90;

    /// <summary>Low disk space threshold percentage before alert triggers (default 10%).</summary>
    [JsonPropertyName("systemHealthLowDiskThreshold")] public int SystemHealthLowDiskThreshold { get; set; } = 10;

    /// <summary>Low battery threshold percentage before alert triggers (default 20%).</summary>
    [JsonPropertyName("systemHealthLowBatteryThreshold")] public int SystemHealthLowBatteryThreshold { get; set; } = 20;

    /// <summary>Whether sustained system health alerts trigger companion reactions.</summary>
    [JsonPropertyName("enableHealthCompanionAlerts")] public bool EnableHealthCompanionAlerts { get; set; } = true;

    /// <summary>Show compact system health pill (CPU & RAM) on the floating Developer HUD.</summary>
    [JsonPropertyName("enableHealthHud")] public bool EnableHealthHud { get; set; } = false;

    /// <summary>List of process names to monitor working set and counts for.</summary>
    [JsonPropertyName("monitoredProcessNames")]
    public List<string> MonitoredProcessNames { get; set; } = new()
    {
        "code", "devenv", "dotnet", "node", "cargo", "docker"
    };

    /// <summary>
    /// Validates all configuration values, clamping numbers within safe limits and falling back
    /// to sensible defaults for corrupt or missing values.
    /// </summary>
    public void Validate()
    {
        SizeScale = Math.Clamp(SizeScale, 0.5, 2.0);
        Speed = Math.Clamp(Speed, 0.3, 2.5);
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        PersonalityMood = Math.Clamp(PersonalityMood, 0.0, 1.0);
        PersonalityEnergy = Math.Clamp(PersonalityEnergy, 0.0, 1.0);
        PersonalityFamiliarity = Math.Clamp(PersonalityFamiliarity, 0.0, 1.0);
        PersonalityMischief = Math.Clamp(PersonalityMischief, 0.0, 1.0);
        if (string.IsNullOrWhiteSpace(ActivePet)) ActivePet = "neku";
        if (PersonalityPreset is not ("Calm" or "Playful" or "Expressive" or "Chaotic" or "Custom"))
            PersonalityPreset = "Expressive";
        if (FocusDurationMinutes <= 0) FocusDurationMinutes = 25;
        if (BreakDurationMinutes <= 0) BreakDurationMinutes = 5;
        if (LongBreakDurationMinutes <= 0) LongBreakDurationMinutes = 15;
        if (LongBreakInterval <= 0) LongBreakInterval = 4;
        if (DailyFocusGoalMinutes <= 0) DailyFocusGoalMinutes = 100;
        if (WeeklyFocusGoalMinutes <= 0) WeeklyFocusGoalMinutes = 500;
        if (DailyTaskGoal <= 0) DailyTaskGoal = 3;
        if (WeeklyTaskGoal <= 0) WeeklyTaskGoal = 15;
        if (ReminderIntervalMinutes <= 0) ReminderIntervalMinutes = 30;
        if (DefaultSnoozeMinutes <= 0) DefaultSnoozeMinutes = 15;
        if (string.IsNullOrWhiteSpace(QuietHoursStart)) QuietHoursStart = "22:00";
        if (string.IsNullOrWhiteSpace(QuietHoursEnd)) QuietHoursEnd = "08:00";
        if (QuickCaptureHotkeyModifiers <= 0) QuickCaptureHotkeyModifiers = 2 | 1;
        if (QuickCaptureHotkeyKey <= 0) QuickCaptureHotkeyKey = 46;
        if (FocusCycleCompletedSessions < 0) FocusCycleCompletedSessions = 0;
        if (CommandPaletteHotkeyModifiers <= 0) CommandPaletteHotkeyModifiers = 2 | 1;
        if (CommandPaletteHotkeyKey <= 0) CommandPaletteHotkeyKey = 59;
        if (AgentMaxSessionsRetained < 5 || AgentMaxSessionsRetained > 500) AgentMaxSessionsRetained = 50;
        if (AgentMaxEventsPerSession < 10 || AgentMaxEventsPerSession > 1000) AgentMaxEventsPerSession = 100;
        IdleThresholdSeconds = Math.Clamp(IdleThresholdSeconds, 30, 3600);
        MonitoredRepositoryPaths ??= new();
        if (DropzoneDefaultAction is not ("Prompt" or "Note" or "Task")) DropzoneDefaultAction = "Prompt";
        SystemHealthCpuThreshold = Math.Clamp(SystemHealthCpuThreshold, 50, 99);
        SystemHealthRamThreshold = Math.Clamp(SystemHealthRamThreshold, 50, 99);
        SystemHealthLowDiskThreshold = Math.Clamp(SystemHealthLowDiskThreshold, 1, 50);
        SystemHealthLowBatteryThreshold = Math.Clamp(SystemHealthLowBatteryThreshold, 5, 50);
        MonitoredProcessNames ??= new() { "code", "devenv", "dotnet", "node", "cargo", "docker" };
    }
}
