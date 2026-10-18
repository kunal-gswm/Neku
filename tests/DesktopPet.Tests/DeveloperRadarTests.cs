using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopPet.Services;
using DesktopPet.Services.Agent;
using DesktopPet.Services.DeskSense;
using DesktopPet.Services.Radar;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class TestGitRunner : IGitRunner
{
    public bool IsGitAvailable { get; set; } = true;
    public string Branch { get; set; } = "main";
    public string StatusOutput { get; set; } = "";
    public int ExitCode { get; set; } = 0;
    public string StandardError { get; set; } = "";

    public Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(string workingDirectory, string[] arguments, CancellationToken ct = default)
    {
        if (!IsGitAvailable)
        {
            return Task.FromResult((-1, "", "git: command not found"));
        }

        string cmd = string.Join(" ", arguments);
        if (cmd.Contains("rev-parse --is-inside-work-tree"))
        {
            return Task.FromResult((ExitCode, "true\n", StandardError));
        }
        if (cmd.Contains("rev-parse --abbrev-ref HEAD"))
        {
            return Task.FromResult((ExitCode, Branch + "\n", StandardError));
        }
        if (cmd.Contains("status --porcelain=v1") || cmd.Contains("status"))
        {
            return Task.FromResult((ExitCode, StatusOutput, StandardError));
        }

        return Task.FromResult((0, "", ""));
    }
}

public class DeveloperRadarTests
{
    [Fact]
    public async Task RepositoryMonitor_DetectsCleanRepository()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new AppSettings();
            var git = new TestGitRunner
            {
                Branch = "main",
                StatusOutput = ""
            };
            var monitor = new RepositoryMonitorService(settings, git);

            bool added = await monitor.AddRepositoryAsync(tempDir);
            Assert.True(added);

            var repo = monitor.GetRepository(tempDir);
            Assert.NotNull(repo);
            Assert.Equal("main", repo.Branch);
            Assert.False(repo.HasUncommittedChanges);
            Assert.Contains("clean", repo.DisplayStatus);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RepositoryMonitor_DetectsDirtyRepository()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new AppSettings();
            var git = new TestGitRunner
            {
                Branch = "feature/desksense",
                StatusOutput = " M src/File.cs\n?? new_file.txt\n"
            };
            var monitor = new RepositoryMonitorService(settings, git);

            await monitor.AddRepositoryAsync(tempDir);
            var repo = monitor.GetRepository(tempDir);

            Assert.NotNull(repo);
            Assert.Equal("feature/desksense", repo.Branch);
            Assert.True(repo.HasUncommittedChanges);
            Assert.Contains("uncommitted", repo.DisplayStatus);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RepositoryMonitor_HandlesMissingGitGracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "neku_test_repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new AppSettings();
            var git = new TestGitRunner { IsGitAvailable = false };
            var monitor = new RepositoryMonitorService(settings, git);

            Assert.False(monitor.IsGitAvailable);

            // Adding repo should succeed without crashing or throwing
            bool added = await monitor.AddRepositoryAsync(tempDir);
            Assert.True(added);

            var repo = monitor.GetRepository(tempDir);
            Assert.NotNull(repo);
            Assert.Equal("Unknown", repo.Branch);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DeveloperRadarService_RecordsBuildAndTestEvents()
    {
        var settings = new AppSettings();
        var deskSense = new DeskSenseService(settings, new TestDeskSenseSignalProvider());
        var repoMonitor = new RepositoryMonitorService(settings, new TestGitRunner());
        var radar = new DeveloperRadarService(settings, deskSense, repoMonitor);

        // Record build success
        radar.RecordBuildTestResult("BackendProject", BuildTestResultStatus.Success, "Local Build", "Build completed in 1.4s");

        var latest = radar.GetLatestBuildRecord();
        Assert.NotNull(latest);
        Assert.Equal("BackendProject", latest.ProjectName);
        Assert.Equal(BuildTestResultStatus.Success, latest.Status);
        Assert.Equal("PASS", latest.StatusBadgeText);

        // Record test failure
        radar.RecordBuildTestResult("FrontendProject", BuildTestResultStatus.Failure, "Test Suite", "2 tests failed");

        var latestFailure = radar.GetLatestBuildRecord();
        Assert.NotNull(latestFailure);
        Assert.Equal("FrontendProject", latestFailure.ProjectName);
        Assert.Equal(BuildTestResultStatus.Failure, latestFailure.Status);
        Assert.Equal("FAIL", latestFailure.StatusBadgeText);

        var allRecords = radar.GetBuildRecords();
        Assert.Equal(2, allRecords.Count);
    }

    [Fact]
    public void DeveloperRadarService_FocusTimerSummary_ReflectsState()
    {
        var settings = new AppSettings();
        var timer = new FocusTimerService(dispatcher: null);
        var deskSense = new DeskSenseService(settings, new TestDeskSenseSignalProvider());
        var repoMonitor = new RepositoryMonitorService(settings, new TestGitRunner());
        var radar = new DeveloperRadarService(settings, deskSense, repoMonitor, focusTimer: timer);

        // Idle state
        Assert.Equal("Idle", radar.GetFocusTimerSummary());

        // Started focus
        timer.StartFocus(25);
        string summary = radar.GetFocusTimerSummary();
        Assert.StartsWith("Focusing", summary);
        Assert.Contains(":", summary);
    }

    [Fact]
    public void DeveloperHudWindow_InitializesAndRendersOnStaThread()
    {
        var thread = new Thread(() =>
        {
            var settings = new AppSettings();
            var deskSense = new DeskSenseService(settings, new TestDeskSenseSignalProvider());
            var repoMonitor = new RepositoryMonitorService(settings, new TestGitRunner());
            var radar = new DeveloperRadarService(settings, deskSense, repoMonitor);

            var hud = new DeveloperHudWindow(settings, radar);
            Assert.NotNull(hud);
            hud.Show();
            Assert.True(hud.IsVisible);
            hud.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));
    }
}
