using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopPet.Native;

namespace DesktopPet.Services.Health;

/// <summary>
/// Lightweight, local-first system resource monitor.
/// Collects CPU, RAM, Disk, Battery, and process metrics with sustained threshold alert detection and hysteresis.
/// Completely local, zero network calls, zero external dependencies.
/// </summary>
public sealed class SystemHealthService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly System.Threading.Timer? _pollTimer;
    private readonly object _lock = new();

    // CPU sampling state
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _haveCpuBaseline;
    private int _currentCpuPercent;

    // Sustained threshold tracking (seconds in critical state)
    private double _cpuCriticalSeconds;
    private double _ramCriticalSeconds;

    // Alert active states (for hysteresis)
    private bool _cpuAlertActive;
    private bool _ramAlertActive;
    private bool _diskAlertActive;
    private bool _batteryAlertActive;

    // Alert cooldown tracking (avoid spamming companion)
    private DateTime _lastCpuAlertTime = DateTime.MinValue;
    private DateTime _lastRamAlertTime = DateTime.MinValue;
    private DateTime _lastDiskAlertTime = DateTime.MinValue;
    private DateTime _lastBatteryAlertTime = DateTime.MinValue;

    private const double AlertCooldownSeconds = 60.0;
    private const double SustainedThresholdRequiredSeconds = 8.0;
    private const int HysteresisBufferPercent = 5;

    public SystemHealthSnapshot CurrentSnapshot { get; private set; } = new();

    public event Action<SystemHealthSnapshot>? HealthUpdated;
    public event Action<SystemHealthAlert>? AlertTriggered;

    public SystemHealthService(AppSettings settings, bool startTimer = true)
    {
        _settings = settings;

        if (startTimer)
        {
            // Poll every 2.5 seconds
            _pollTimer = new System.Threading.Timer(_ => Poll(), null, 500, 2500);
        }
    }

    /// <summary>
    /// Executes a single polling pass over local system health metrics.
    /// Thread-safe and callable directly by automated unit tests.
    /// </summary>
    public SystemHealthSnapshot Poll(double elapsedSeconds = 2.5)
    {
        lock (_lock)
        {
            if (!_settings.EnableSystemHealthMonitoring)
            {
                return CurrentSnapshot;
            }

            int cpu = SampleCpu();
            var (memPercent, totalMem, availMem) = SampleMemory();
            var drives = SampleDrives();
            var (hasBattery, batteryPercent, isCharging) = SampleBattery();
            var processes = SampleProcesses();

            // Evaluate sustained threshold conditions & hysteresis
            bool cpuCrit = EvaluateCpuAlert(cpu, elapsedSeconds);
            bool ramCrit = EvaluateRamAlert(memPercent, elapsedSeconds);
            bool diskCrit = EvaluateDiskAlert(drives);
            bool battCrit = EvaluateBatteryAlert(hasBattery, batteryPercent, isCharging);

            var snapshot = new SystemHealthSnapshot
            {
                Timestamp = DateTime.UtcNow,
                CpuPercent = cpu,
                MemoryPercent = memPercent,
                TotalMemoryBytes = totalMem,
                AvailableMemoryBytes = availMem,
                Drives = drives,
                HasBattery = hasBattery,
                BatteryPercent = batteryPercent,
                IsCharging = isCharging,
                Processes = processes,
                IsCpuCritical = cpuCrit,
                IsMemoryCritical = ramCrit,
                IsDiskCritical = diskCrit,
                IsBatteryCritical = battCrit
            };

            CurrentSnapshot = snapshot;
            HealthUpdated?.Invoke(snapshot);
            return snapshot;
        }
    }

    private int SampleCpu()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return _currentCpuPercent;

            ulong i = ToU64(idle), k = ToU64(kernel), u = ToU64(user);
            if (_haveCpuBaseline)
            {
                ulong di = i - _lastIdle, dk = k - _lastKernel, du = u - _lastUser;
                ulong total = dk + du; // kernel time already includes idle
                if (total > 0)
                {
                    double load = Math.Clamp(1.0 - (double)di / total, 0, 1);
                    _currentCpuPercent = (int)Math.Round(load * 100);
                }
            }
            _lastIdle = i; _lastKernel = k; _lastUser = u;
            _haveCpuBaseline = true;
            return _currentCpuPercent;
        }
        catch
        {
            return _currentCpuPercent;
        }
    }

    private static (int percent, long totalBytes, long availBytes) SampleMemory()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                int percent = (int)Math.Clamp(mem.dwMemoryLoad, 0, 100);
                return (percent, (long)mem.ullTotalPhys, (long)mem.ullAvailPhys);
            }
        }
        catch { }

        return (0, 0, 0);
    }

    private static List<DiskDriveInfo> SampleDrives()
    {
        var result = new List<DiskDriveInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                    {
                        result.Add(new DiskDriveInfo
                        {
                            Name = drive.Name.TrimEnd('\\'),
                            VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                            TotalBytes = drive.TotalSize,
                            FreeBytes = drive.TotalFreeSpace
                        });
                    }
                }
                catch { /* Ignore unready, bitlocker-locked, or restricted drives */ }
            }
        }
        catch { }
        return result;
    }

    private static (bool hasBattery, int percent, bool isCharging) SampleBattery()
    {
        try
        {
            var p = SystemInformation.PowerStatus;
            bool hasBattery = !p.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery);
            if (!hasBattery) return (false, -1, false);

            float life = p.BatteryLifePercent;
            int percent = (life >= 0 && life <= 1) ? (int)Math.Round(life * 100) : -1;
            bool charging = p.PowerLineStatus == PowerLineStatus.Online ||
                            p.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);

            return (true, percent, charging);
        }
        catch
        {
            return (false, -1, false);
        }
    }

    private List<MonitoredProcessSummary> SampleProcesses()
    {
        var summaries = new List<MonitoredProcessSummary>();
        var names = _settings.MonitoredProcessNames ?? new();

        foreach (var rawName in names)
        {
            string procName = rawName.Trim();
            if (string.IsNullOrWhiteSpace(procName)) continue;

            try
            {
                var procs = Process.GetProcessesByName(procName);
                if (procs.Length > 0)
                {
                    long totalBytes = 0;
                    foreach (var p in procs)
                    {
                        try { totalBytes += p.WorkingSet64; } catch { }
                        p.Dispose();
                    }

                    summaries.Add(new MonitoredProcessSummary
                    {
                        ProcessName = procName,
                        InstanceCount = procs.Length,
                        TotalWorkingSetBytes = totalBytes
                    });
                }
            }
            catch { }
        }

        return summaries;
    }

    public List<SystemHealthAlert> EvaluateThresholds(SystemHealthSnapshot snapshot)
    {
        var alerts = new List<SystemHealthAlert>();
        if (snapshot.CpuPercent >= _settings.SystemHealthCpuThreshold)
        {
            alerts.Add(new SystemHealthAlert
            {
                Metric = SystemHealthMetric.Cpu,
                Value = snapshot.CpuPercent,
                Message = $"High CPU load: {snapshot.CpuPercent}%"
            });
        }
        if (snapshot.MemoryPercent >= _settings.SystemHealthRamThreshold)
        {
            alerts.Add(new SystemHealthAlert
            {
                Metric = SystemHealthMetric.Memory,
                Value = snapshot.MemoryPercent,
                Message = $"High RAM load: {snapshot.MemoryPercent}%"
            });
        }
        return alerts;
    }

    public bool EvaluateCpuAlert(int cpu, double dt)
    {
        int threshold = _settings.SystemHealthCpuThreshold;
        int clearThreshold = Math.Max(0, threshold - HysteresisBufferPercent);

        if (cpu >= threshold)
        {
            _cpuCriticalSeconds += dt;
            if (_cpuCriticalSeconds >= SustainedThresholdRequiredSeconds)
            {
                if (!_cpuAlertActive || (DateTime.UtcNow - _lastCpuAlertTime).TotalSeconds > AlertCooldownSeconds)
                {
                    _cpuAlertActive = true;
                    _lastCpuAlertTime = DateTime.UtcNow;
                    AlertTriggered?.Invoke(new SystemHealthAlert
                    {
                        Metric = SystemHealthMetric.Cpu,
                        Value = cpu,
                        Message = $"High CPU load sustained at {cpu}%"
                    });
                }
                return true;
            }
        }
        else if (cpu < clearThreshold)
        {
            _cpuCriticalSeconds = 0;
            _cpuAlertActive = false;
        }

        return _cpuAlertActive;
    }

    public bool EvaluateRamAlert(int ram, double dt)
    {
        int threshold = _settings.SystemHealthRamThreshold;
        int clearThreshold = Math.Max(0, threshold - HysteresisBufferPercent);

        if (ram >= threshold)
        {
            _ramCriticalSeconds += dt;
            if (_ramCriticalSeconds >= SustainedThresholdRequiredSeconds)
            {
                if (!_ramAlertActive || (DateTime.UtcNow - _lastRamAlertTime).TotalSeconds > AlertCooldownSeconds)
                {
                    _ramAlertActive = true;
                    _lastRamAlertTime = DateTime.UtcNow;
                    AlertTriggered?.Invoke(new SystemHealthAlert
                    {
                        Metric = SystemHealthMetric.Memory,
                        Value = ram,
                        Message = $"High Memory usage sustained at {ram}%"
                    });
                }
                return true;
            }
        }
        else if (ram < clearThreshold)
        {
            _ramCriticalSeconds = 0;
            _ramAlertActive = false;
        }

        return _ramAlertActive;
    }

    private bool EvaluateDiskAlert(List<DiskDriveInfo> drives)
    {
        int thresholdPercent = _settings.SystemHealthLowDiskThreshold;
        bool anyCritical = false;

        foreach (var drive in drives)
        {
            if (drive.TotalBytes > 0)
            {
                double freePercent = ((double)drive.FreeBytes / drive.TotalBytes) * 100.0;
                if (freePercent <= thresholdPercent || drive.FreeGb < 2.0)
                {
                    anyCritical = true;
                    if (!_diskAlertActive || (DateTime.UtcNow - _lastDiskAlertTime).TotalSeconds > AlertCooldownSeconds)
                    {
                        _diskAlertActive = true;
                        _lastDiskAlertTime = DateTime.UtcNow;
                        AlertTriggered?.Invoke(new SystemHealthAlert
                        {
                            Metric = SystemHealthMetric.Disk,
                            Value = (int)Math.Round(freePercent),
                            Message = $"Low disk space on {drive.Name} ({drive.FreeGb} GB free)"
                        });
                    }
                    break;
                }
            }
        }

        if (!anyCritical)
        {
            _diskAlertActive = false;
        }

        return anyCritical;
    }

    private bool EvaluateBatteryAlert(bool hasBattery, int batteryPercent, bool isCharging)
    {
        if (!hasBattery || isCharging || batteryPercent < 0)
        {
            _batteryAlertActive = false;
            return false;
        }

        int threshold = _settings.SystemHealthLowBatteryThreshold;
        if (batteryPercent <= threshold)
        {
            if (!_batteryAlertActive || (DateTime.UtcNow - _lastBatteryAlertTime).TotalSeconds > AlertCooldownSeconds)
            {
                _batteryAlertActive = true;
                _lastBatteryAlertTime = DateTime.UtcNow;
                AlertTriggered?.Invoke(new SystemHealthAlert
                {
                    Metric = SystemHealthMetric.Battery,
                    Value = batteryPercent,
                    Message = $"Low battery alert at {batteryPercent}%"
                });
            }
            return true;
        }
        else if (batteryPercent > threshold + HysteresisBufferPercent)
        {
            _batteryAlertActive = false;
        }

        return _batteryAlertActive;
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }

    // ── Native Interop ────────────────────────────────────────────────────────
    private static ulong ToU64(FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public int dwLowDateTime; public int dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
