using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace DesktopPet.Services.Radar;

public interface IGitRunner
{
    bool IsGitAvailable { get; }
    Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(string workingDirectory, string[] arguments, CancellationToken ct = default);
}

public sealed class CliGitRunner : IGitRunner
{
    private static bool? _cachedGitAvailable;

    public bool IsGitAvailable
    {
        get
        {
            if (_cachedGitAvailable.HasValue) return _cachedGitAvailable.Value;
            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("--version");

                using var proc = Process.Start(psi);
                if (proc != null && proc.WaitForExit(2000))
                {
                    _cachedGitAvailable = proc.ExitCode == 0;
                    return _cachedGitAvailable.Value;
                }
            }
            catch
            {
                _cachedGitAvailable = false;
            }
            _cachedGitAvailable = false;
            return false;
        }
    }

    public async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        string workingDirectory, string[] arguments, CancellationToken ct = default)
    {
        if (!IsGitAvailable)
        {
            return (-1, "", "Git is not installed or available in PATH.");
        }

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            return (proc.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch { }
            return (-1, "", "Git command timed out.");
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}

public sealed class RepositoryMonitorService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IGitRunner _gitRunner;
    private readonly object _lock = new();
    private readonly Dictionary<string, MonitoredRepository> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    private bool _isPaused;
    private bool _disposed;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            _settings.PauseRepositoryMonitoring = value;
        }
    }

    public bool IsGitAvailable => _gitRunner.IsGitAvailable;

    public event Action<MonitoredRepository>? RepositoryUpdated;
    public event Action? RepositoriesChanged;

    public RepositoryMonitorService(AppSettings settings, IGitRunner? gitRunner = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _gitRunner = gitRunner ?? new CliGitRunner();
        _isPaused = _settings.PauseRepositoryMonitoring;

        InitializeFromSettings();
    }

    private void InitializeFromSettings()
    {
        lock (_lock)
        {
            foreach (var path in _settings.MonitoredRepositoryPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string normalized = Path.GetFullPath(path.Trim());
                if (!_repositories.ContainsKey(normalized))
                {
                    var repo = new MonitoredRepository
                    {
                        Path = normalized,
                        Name = Path.GetFileName(normalized)
                    };
                    _repositories[normalized] = repo;
                    SetupWatcher(normalized);
                }
            }
        }
    }

    public IReadOnlyList<MonitoredRepository> GetRepositories()
    {
        lock (_lock)
        {
            return _repositories.Values.ToList();
        }
    }

    public MonitoredRepository? GetRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string normalized = Path.GetFullPath(path.Trim());
        lock (_lock)
        {
            return _repositories.TryGetValue(normalized, out var r) ? r : null;
        }
    }

    public async Task<bool> AddRepositoryAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(normalized)) return false;
        }
        catch
        {
            return false;
        }

        MonitoredRepository repo;
        lock (_lock)
        {
            if (_repositories.ContainsKey(normalized)) return true;

            repo = new MonitoredRepository
            {
                Path = normalized,
                Name = Path.GetFileName(normalized)
            };
            _repositories[normalized] = repo;

            if (!_settings.MonitoredRepositoryPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                _settings.MonitoredRepositoryPaths.Add(normalized);
            }

            SetupWatcher(normalized);
        }

        RepositoriesChanged?.Invoke();

        await RefreshRepositoryAsync(normalized).ConfigureAwait(false);
        return true;
    }

    public void AddRepository(string path) => _ = AddRepositoryAsync(path);

    public void RemoveRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string normalized = Path.GetFullPath(path.Trim());

        lock (_lock)
        {
            if (_repositories.Remove(normalized))
            {
                _settings.MonitoredRepositoryPaths.RemoveAll(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));

                if (_watchers.TryGetValue(normalized, out var watcher))
                {
                    watcher.Dispose();
                    _watchers.Remove(normalized);
                }

                if (_debounceTimers.TryRemove(normalized, out var timer))
                {
                    timer.Dispose();
                }
            }
        }

        RepositoriesChanged?.Invoke();
    }

    public async Task RefreshAllAsync()
    {
        if (_isPaused || !_gitRunner.IsGitAvailable) return;

        List<string> paths;
        lock (_lock)
        {
            paths = _repositories.Keys.ToList();
        }

        foreach (var path in paths)
        {
            if (_cts.IsCancellationRequested) break;
            await RefreshRepositoryAsync(path).ConfigureAwait(false);
        }
    }

    public async Task<MonitoredRepository> RefreshRepositoryAsync(string path)
    {
        string normalized = Path.GetFullPath(path.Trim());
        MonitoredRepository? repo;

        lock (_lock)
        {
            if (!_repositories.TryGetValue(normalized, out repo))
            {
                repo = new MonitoredRepository
                {
                    Path = normalized,
                    Name = Path.GetFileName(normalized)
                };
                _repositories[normalized] = repo;
            }
        }

        if (!Directory.Exists(normalized))
        {
            repo.IsAccessible = false;
            repo.IsGitRepository = false;
            repo.ErrorMessage = "Directory does not exist.";
            repo.LastChecked = DateTimeOffset.UtcNow;
            RepositoryUpdated?.Invoke(repo);
            return repo;
        }

        repo.IsAccessible = true;

        if (!_gitRunner.IsGitAvailable)
        {
            repo.IsGitRepository = false;
            repo.ErrorMessage = "Git is not installed or available.";
            repo.LastChecked = DateTimeOffset.UtcNow;
            RepositoryUpdated?.Invoke(repo);
            return repo;
        }

        try
        {
            // 1. Verify it's a git repo
            var (revCode, revOut, _) = await _gitRunner.RunGitAsync(
                normalized, new[] { "rev-parse", "--is-inside-work-tree" }, _cts.Token).ConfigureAwait(false);

            if (revCode != 0 || !revOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                repo.IsGitRepository = false;
                repo.ErrorMessage = "Not a Git working tree.";
                repo.Branch = "None";
                repo.HasUncommittedChanges = false;
                repo.UncommittedSummary = "";
                repo.LastChecked = DateTimeOffset.UtcNow;
                RepositoryUpdated?.Invoke(repo);
                return repo;
            }

            repo.IsGitRepository = true;
            repo.ErrorMessage = null;

            // 2. Query branch
            var (branchCode, branchOut, _) = await _gitRunner.RunGitAsync(
                normalized, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, _cts.Token).ConfigureAwait(false);

            repo.Branch = branchCode == 0 && !string.IsNullOrWhiteSpace(branchOut)
                ? branchOut.Trim()
                : "HEAD (detached)";

            // 3. Query porcelain status
            var (statusCode, statusOut, _) = await _gitRunner.RunGitAsync(
                normalized, new[] { "status", "--porcelain=v1" }, _cts.Token).ConfigureAwait(false);

            if (statusCode == 0)
            {
                if (string.IsNullOrWhiteSpace(statusOut))
                {
                    repo.HasUncommittedChanges = false;
                    repo.UncommittedSummary = "Clean";
                }
                else
                {
                    var lines = statusOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    repo.HasUncommittedChanges = lines.Length > 0;
                    repo.UncommittedSummary = $"{lines.Length} uncommitted file(s)";
                }
            }
        }
        catch (Exception ex)
        {
            repo.ErrorMessage = ex.Message;
        }

        repo.LastChecked = DateTimeOffset.UtcNow;
        RepositoryUpdated?.Invoke(repo);
        return repo;
    }

    private void SetupWatcher(string repoPath)
    {
        try
        {
            string gitDir = Path.Combine(repoPath, ".git");
            string watchPath = Directory.Exists(gitDir) ? gitDir : repoPath;

            var watcher = new FileSystemWatcher(watchPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, _) => OnRepoFilesystemChanged(repoPath);
            watcher.Created += (_, _) => OnRepoFilesystemChanged(repoPath);
            watcher.Deleted += (_, _) => OnRepoFilesystemChanged(repoPath);
            watcher.Renamed += (_, _) => OnRepoFilesystemChanged(repoPath);

            _watchers[repoPath] = watcher;
        }
        catch
        {
            // Fallback gracefully if filesystem events cannot be raised (e.g. network share)
        }
    }

    private void OnRepoFilesystemChanged(string repoPath)
    {
        if (_isPaused || _disposed) return;

        // Debounce: 1 second delay
        _debounceTimers.AddOrUpdate(repoPath,
            path => new Timer(_ => _ = RefreshRepositoryAsync(path), null, 1000, Timeout.Infinite),
            (path, existingTimer) =>
            {
                existingTimer.Change(1000, Timeout.Infinite);
                return existingTimer;
            });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();

        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                try { watcher.Dispose(); } catch { }
            }
            _watchers.Clear();

            foreach (var timer in _debounceTimers.Values)
            {
                try { timer.Dispose(); } catch { }
            }
            _debounceTimers.Clear();
        }

        _cts.Dispose();
    }
}
