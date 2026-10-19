namespace DesktopPet.Services.Health;

/// <summary>
/// Metric categories monitored by the System Health Service.
/// </summary>
public enum SystemHealthMetric
{
    Cpu,
    Memory,
    Disk,
    Battery
}

/// <summary>
/// Snapshot of a single disk drive partition.
/// </summary>
public sealed class DiskDriveInfo
{
    public string Name { get; init; } = "";
    public string VolumeLabel { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public double TotalGb => Math.Round(TotalBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double FreeGb => Math.Round(FreeBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double UsedGb => Math.Max(0, Math.Round((TotalBytes - FreeBytes) / (1024.0 * 1024.0 * 1024.0), 1));
    public int UsedPercent => TotalBytes <= 0 ? 0 : (int)Math.Clamp(Math.Round(((double)(TotalBytes - FreeBytes) / TotalBytes) * 100.0), 0, 100);
}

/// <summary>
/// Snapshot of a monitored local process.
/// </summary>
public sealed class MonitoredProcessSummary
{
    public string ProcessName { get; init; } = "";
    public int InstanceCount { get; init; }
    public long TotalWorkingSetBytes { get; init; }

    public double WorkingSetMb => Math.Round(TotalWorkingSetBytes / (1024.0 * 1024.0), 1);
}

/// <summary>
/// Full snapshot of system health metrics collected locally.
/// </summary>
public sealed class SystemHealthSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // CPU (0..100)
    public int CpuPercent { get; init; }

    // RAM (0..100, MBs)
    public int MemoryPercent { get; init; }
    public long TotalMemoryBytes { get; init; }
    public long AvailableMemoryBytes { get; init; }

    public double TotalMemoryGb => Math.Round(TotalMemoryBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public double UsedMemoryGb => Math.Max(0, Math.Round((TotalMemoryBytes - AvailableMemoryBytes) / (1024.0 * 1024.0 * 1024.0), 1));

    // Disk
    public List<DiskDriveInfo> Drives { get; init; } = new();

    // Battery & Power
    public bool HasBattery { get; init; }
    public int BatteryPercent { get; init; } = -1;
    public bool IsCharging { get; init; }

    // Monitored processes
    public List<MonitoredProcessSummary> Processes { get; init; } = new();

    // Threshold Alert Flags
    public bool IsCpuCritical { get; init; }
    public bool IsMemoryCritical { get; init; }
    public bool IsDiskCritical { get; init; }
    public bool IsBatteryCritical { get; init; }
}

/// <summary>
/// Alert event payload raised when a sustained health threshold is crossed.
/// </summary>
public sealed class SystemHealthAlert
{
    public SystemHealthMetric Metric { get; init; }
    public int Value { get; init; }
    public string Message { get; init; } = "";
    public DateTime TriggeredAt { get; init; } = DateTime.UtcNow;
}
