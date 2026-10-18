using System.IO;
using System.Text.Json;

namespace DesktopPet.Services;

/// <summary>
/// Local-first persistent storage for Pomodoro and focus session history.
/// Saves to %AppData%\Neku\focus_history.json with atomic writes and corruption recovery.
/// </summary>
public sealed class FocusHistoryService
{
    private readonly string _filePath;
    private readonly List<FocusSessionRecord> _sessions = new();
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IReadOnlyList<FocusSessionRecord> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.ToList();
            }
        }
    }

    public event Action<FocusSessionRecord>? SessionAdded;
    public event Action? HistoryChanged;

    public string? LastCorruptBackupPath { get; private set; }

    public FocusHistoryService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "focus_history.json");
        }

        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _sessions.Clear();

            // Check if orphaned temp file exists from interrupted move
            if (!File.Exists(_filePath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(_filePath)!;
                    string baseName = Path.GetFileName(_filePath);
                    if (Directory.Exists(dir))
                    {
                        var candidates = Directory.GetFiles(dir, baseName + ".tmp.*")
                                                  .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                                  .ToList();
                        if (candidates.Count > 0 && new FileInfo(candidates[0]).Length > 0)
                        {
                            File.Move(candidates[0], _filePath, overwrite: true);
                            DebugLog.Write($"[FocusHistoryService] Recovered history file from interrupted atomic write: {candidates[0]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[FocusHistoryService] Error recovering temp file: {ex.Message}");
                }
            }

            if (!File.Exists(_filePath))
            {
                return;
            }

            bool parseSuccess = false;
            try
            {
                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                try
                {
                    var doc = JsonSerializer.Deserialize<FocusHistoryDocument>(json, JsonOpts);
                    if (doc?.Sessions != null)
                    {
                        foreach (var s in doc.Sessions)
                        {
                            ValidateAndAdd(s);
                        }
                        parseSuccess = true;
                        return;
                    }
                }
                catch
                {
                    // Fallback to raw list
                }

                try
                {
                    var rawList = JsonSerializer.Deserialize<List<FocusSessionRecord>>(json, JsonOpts);
                    if (rawList != null)
                    {
                        foreach (var s in rawList)
                        {
                            ValidateAndAdd(s);
                        }
                        parseSuccess = true;
                        return;
                    }
                }
                catch
                {
                }

                if (!parseSuccess)
                {
                    BackupCorruptFile();
                    _sessions.Clear();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[FocusHistoryService] Failed to read focus history: {ex.Message}");
                BackupCorruptFile();
                _sessions.Clear();
            }
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(_filePath) && new FileInfo(_filePath).Length > 0)
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string backup = _filePath + ".corrupt." + timestamp;
                if (File.Exists(backup))
                {
                    backup += "_" + Guid.NewGuid().ToString("N")[..6];
                }
                File.Copy(_filePath, backup, overwrite: true);
                LastCorruptBackupPath = backup;
                DebugLog.Write($"[FocusHistoryService] Preserved malformed history file to: {backup}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[FocusHistoryService] Failed to backup malformed history: {ex.Message}");
        }
    }

    private void ValidateAndAdd(FocusSessionRecord? s)
    {
        if (s == null) return;
        if (string.IsNullOrWhiteSpace(s.Id))
        {
            s.Id = Guid.NewGuid().ToString("N");
        }
        if (s.ActualSeconds < 0) s.ActualSeconds = 0;
        if (s.PausedSeconds < 0) s.PausedSeconds = 0;
        if (s.TargetMinutes < 1) s.TargetMinutes = 25;

        _sessions.Add(s);
    }

    public void Save()
    {
        lock (_lock)
        {
            string? tempPath = null;
            try
            {
                string dir = Path.GetDirectoryName(_filePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var doc = new FocusHistoryDocument
                {
                    Version = 1,
                    Sessions = _sessions.ToList()
                };

                string json = JsonSerializer.Serialize(doc, JsonOpts);

                tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
                tempPath = null;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[FocusHistoryService] Failed to save history: {ex.Message}");
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }

    public void RecordSession(FocusSessionRecord record)
    {
        if (record == null) return;

        lock (_lock)
        {
            _sessions.Insert(0, record);
        }

        Save();
        SessionAdded?.Invoke(record);
        HistoryChanged?.Invoke();
    }

    public IReadOnlyList<FocusSessionRecord> GetSessionsInDateRange(DateTime startLocal, DateTime endLocal)
    {
        lock (_lock)
        {
            return _sessions.Where(s =>
            {
                var local = s.StartLocal;
                return local >= startLocal && local <= endLocal;
            }).OrderByDescending(s => s.StartUtc).ToList();
        }
    }

    public IReadOnlyList<FocusSessionRecord> GetTodaySessions()
    {
        var start = DateTime.Today;
        var end = DateTime.Today.AddDays(1).AddTicks(-1);
        return GetSessionsInDateRange(start, end);
    }

    public double GetTodayFocusMinutes()
    {
        var todaySessions = GetTodaySessions();
        return Math.Round(todaySessions
            .Where(s => s.SessionType == FocusSessionType.Focus && s.Status == FocusSessionStatus.Completed)
            .Sum(s => s.ActualSeconds) / 60.0, 1);
    }

    public int GetTodayCompletedFocusCount()
    {
        return GetTodaySessions()
            .Count(s => s.SessionType == FocusSessionType.Focus && s.Status == FocusSessionStatus.Completed);
    }

    public bool UpdateNotes(string sessionId, string notes)
    {
        lock (_lock)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return false;
            session.Notes = notes?.Trim();
        }

        Save();
        HistoryChanged?.Invoke();
        return true;
    }

    public int ClearAll()
    {
        int count;
        lock (_lock)
        {
            count = _sessions.Count;
            _sessions.Clear();
        }

        Save();
        HistoryChanged?.Invoke();
        return count;
    }
}
