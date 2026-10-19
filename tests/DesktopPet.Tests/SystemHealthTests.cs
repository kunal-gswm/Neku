using System;
using System.Collections.Generic;
using System.Linq;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.Health;
using Xunit;

namespace DesktopPet.Tests;

public class SystemHealthTests
{
    [Fact]
    public void Snapshot_calculates_properties_accurately()
    {
        var disks = new List<DiskDriveInfo>
        {
            new DiskDriveInfo { Name = "C:", VolumeLabel = "System", FreeBytes = 100_000_000_000, TotalBytes = 500_000_000_000 }
        };
        var procs = new List<MonitoredProcessSummary>
        {
            new MonitoredProcessSummary { ProcessName = "devenv", InstanceCount = 1, TotalWorkingSetBytes = 1_200_000_000 },
            new MonitoredProcessSummary { ProcessName = "dotnet", InstanceCount = 2, TotalWorkingSetBytes = 450_000_000 }
        };

        var snapshot = new SystemHealthSnapshot
        {
            CpuPercent = 42,
            MemoryPercent = 65,
            TotalMemoryBytes = 32_000_000_000,
            AvailableMemoryBytes = 11_200_000_000,
            Drives = disks,
            Processes = procs,
            BatteryPercent = 88,
            HasBattery = true,
            IsCharging = true
        };

        Assert.Equal(42, snapshot.CpuPercent);
        Assert.Equal(65, snapshot.MemoryPercent);
        Assert.Equal(29.8, snapshot.TotalMemoryGb, 1);
        Assert.Equal(19.4, snapshot.UsedMemoryGb, 1);
        Assert.Single(snapshot.Drives);
        Assert.Equal(80, snapshot.Drives[0].UsedPercent);
        Assert.Equal(2, snapshot.Processes.Count);
        Assert.Equal(88, snapshot.BatteryPercent);
        Assert.True(snapshot.HasBattery);
        Assert.True(snapshot.IsCharging);
    }

    [Fact]
    public void Service_tracks_sustained_threshold_and_hysteresis()
    {
        var settings = new AppSettings
        {
            EnableSystemHealthMonitoring = true,
            SystemHealthCpuThreshold = 80,
            SystemHealthRamThreshold = 80
        };

        using var service = new SystemHealthService(settings);

        var lowSnapshot = new SystemHealthSnapshot { CpuPercent = 25, MemoryPercent = 40 };
        var candidateAlerts = service.EvaluateThresholds(lowSnapshot);
        Assert.Empty(candidateAlerts);

        // High load candidate appears
        var highSnapshot = new SystemHealthSnapshot { CpuPercent = 90, MemoryPercent = 40 };
        candidateAlerts = service.EvaluateThresholds(highSnapshot);
        Assert.Single(candidateAlerts);
        Assert.Equal(SystemHealthMetric.Cpu, candidateAlerts[0].Metric);
    }

    [Fact]
    public void Personality_provides_contextual_health_alert_lines()
    {
        var neku = new Personality("neku");
        string nekuCpu = neku.GetHealthAlertLine(SystemHealthMetric.Cpu);
        string nekuRam = neku.GetHealthAlertLine(SystemHealthMetric.Memory);
        string nekuDisk = neku.GetHealthAlertLine(SystemHealthMetric.Disk);
        string nekuBattery = neku.GetHealthAlertLine(SystemHealthMetric.Battery);

        Assert.False(string.IsNullOrWhiteSpace(nekuCpu));
        Assert.False(string.IsNullOrWhiteSpace(nekuRam));
        Assert.False(string.IsNullOrWhiteSpace(nekuDisk));
        Assert.False(string.IsNullOrWhiteSpace(nekuBattery));

        var billu = new Personality("billu");
        string billuCpu = billu.GetHealthAlertLine(SystemHealthMetric.Cpu);
        string billuRam = billu.GetHealthAlertLine(SystemHealthMetric.Memory);

        Assert.False(string.IsNullOrWhiteSpace(billuCpu));
        Assert.False(string.IsNullOrWhiteSpace(billuRam));

        // Billu has distinct sarcastic/grumpy reactions
        Assert.NotEqual(nekuCpu, billuCpu);
    }

    [Fact]
    public void Process_monitoring_respects_custom_process_names()
    {
        var settings = new AppSettings
        {
            MonitoredProcessNames = new List<string> { "code", "rider", "devenv", "pwsh" }
        };

        Assert.Contains("code", settings.MonitoredProcessNames);
        Assert.Contains("pwsh", settings.MonitoredProcessNames);
        Assert.Equal(4, settings.MonitoredProcessNames.Count);
    }

    [Fact]
    public void Disk_used_percent_and_free_gb_calculations()
    {
        long free = 25_000_000_000;
        long total = 100_000_000_000;
        var disk = new DiskDriveInfo { Name = "D:", VolumeLabel = "Data", FreeBytes = free, TotalBytes = total };

        Assert.Equal(75, disk.UsedPercent);
        Assert.Equal(23.3, disk.FreeGb, 1);
        Assert.Equal(93.1, disk.TotalGb, 1);
    }

    [Fact]
    public void Service_does_not_sample_when_disabled()
    {
        var settings = new AppSettings { EnableSystemHealthMonitoring = false };
        using var service = new SystemHealthService(settings, startTimer: false);

        var snapshot = service.Poll();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Empty(snapshot.Drives);
    }

    [Fact]
    public void Service_sustained_threshold_requires_sufficient_duration_and_hysteresis()
    {
        var settings = new AppSettings
        {
            EnableSystemHealthMonitoring = true,
            SystemHealthCpuThreshold = 80
        };
        using var service = new SystemHealthService(settings, startTimer: false);

        int alertCount = 0;
        service.AlertTriggered += _ => alertCount++;

        // Step 1: 3 seconds of high CPU - should not trigger yet (< 8.0s required)
        bool crit1 = service.EvaluateCpuAlert(85, 3.0);
        Assert.False(crit1);
        Assert.Equal(0, alertCount);

        // Step 2: 3 more seconds (total 6s) - still should not trigger (< 8.0s)
        bool crit2 = service.EvaluateCpuAlert(85, 3.0);
        Assert.False(crit2);
        Assert.Equal(0, alertCount);

        // Step 3: 3 more seconds (total 9s) - reaches sustained threshold!
        bool crit3 = service.EvaluateCpuAlert(85, 3.0);
        Assert.True(crit3);
        Assert.Equal(1, alertCount);

        // Step 4: Drop to 78% (above 75% clear threshold) - remains active due to 5% hysteresis
        bool crit4 = service.EvaluateCpuAlert(78, 1.0);
        Assert.True(crit4);

        // Step 5: Drop to 70% (below 75% clear threshold) - hysteresis clears active state
        bool crit5 = service.EvaluateCpuAlert(70, 1.0);
        Assert.False(crit5);
    }
}
