using System;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DesktopPet.Services.Radar;

public enum BuildTestResultStatus
{
    Unknown,
    Success,
    Failure,
    Pending
}

/// <summary>
/// Status record for a monitored local Git repository.
/// </summary>
public sealed class MonitoredRepository
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Branch { get; set; } = "Unknown";
    public bool HasUncommittedChanges { get; set; }
    public string UncommittedSummary { get; set; } = "";
    public bool IsGitRepository { get; set; }
    public bool IsAccessible { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayStatus =>
        !IsAccessible ? "Inaccessible" :
        !IsGitRepository ? "Not a Git repo" :
        HasUncommittedChanges ? $"{Branch} (uncommitted changes)" :
        $"{Branch} (clean)";

    public Brush StatusBrush =>
        !IsAccessible || !IsGitRepository ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) :
        HasUncommittedChanges ? new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)) :
        new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
}

/// <summary>
/// Build or test status entry tracked by Developer Radar.
/// </summary>
public sealed class BuildTestStatusRecord
{
    public string ProjectName { get; init; } = "";
    public BuildTestResultStatus Status { get; init; } = BuildTestResultStatus.Unknown;
    public string Source { get; init; } = "Unknown";
    public string Message { get; init; } = "";
    public string Confidence { get; init; } = "High";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string StatusBadgeText => Status switch
    {
        BuildTestResultStatus.Success => "PASS",
        BuildTestResultStatus.Failure => "FAIL",
        BuildTestResultStatus.Pending => "RUNNING",
        _ => "INFO"
    };

    public Brush StatusBrush => Status switch
    {
        BuildTestResultStatus.Success => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        BuildTestResultStatus.Failure => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        BuildTestResultStatus.Pending => new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)),
        _ => new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA))
    };
}

