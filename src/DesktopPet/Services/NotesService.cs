using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

public sealed class NoteItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Local notes persistence service. Notes are stored exclusively on disk in %AppData%\Neku\notes.json.
/// No network calls, telemetry, or server syncing.
/// </summary>
public sealed class NotesService
{
    private readonly string _filePath;
    private readonly List<NoteItem> _notes = new();
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string? LastCorruptBackupPath { get; private set; }

    public IReadOnlyList<NoteItem> Notes
    {
        get
        {
            lock (_lock)
            {
                return _notes.ToList();
            }
        }
    }

    public event Action? NotesChanged;

    public NotesService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "notes.json");
        }

        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _notes.Clear();

            // Check if primary file is missing but an orphaned temp file exists from an interrupted move
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
                            DebugLog.Write($"[NotesService] Recovered notes file from interrupted atomic write: {candidates[0]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[NotesService] Error recovering temp file: {ex.Message}");
                }
            }

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var items = JsonSerializer.Deserialize<List<NoteItem>>(json, JsonOpts);
                if (items != null)
                {
                    _notes.AddRange(items.OrderByDescending(n => n.UpdatedAt));
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[NotesService] Deserialization failed: {ex.Message}. Backing up corrupted file.");
                try
                {
                    string corruptDir = Path.GetDirectoryName(_filePath)!;
                    string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    LastCorruptBackupPath = Path.Combine(corruptDir, $"notes.json.corrupt.{timeStamp}");
                    File.Copy(_filePath, LastCorruptBackupPath, overwrite: true);
                }
                catch
                {
                    // Non-fatal
                }

                _notes.Clear();
            }
        }
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

                string json = JsonSerializer.Serialize(_notes, JsonOpts);
                tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
                tempPath = null;
            }
            catch
            {
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }

    public NoteItem Create(string title = "Untitled", string content = "")
    {
        var note = new NoteItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim(),
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _notes.Insert(0, note);
        }

        Save();
        NotesChanged?.Invoke();
        return note;
    }

    public void Update(string id, string title, string content)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            if (note != null)
            {
                note.Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
                note.Content = content;
                note.UpdatedAt = DateTime.UtcNow;
            }
        }

        Save();
        NotesChanged?.Invoke();
    }

    public bool Delete(string id)
    {
        bool removed = false;
        lock (_lock)
        {
            int index = _notes.FindIndex(n => n.Id == id);
            if (index >= 0)
            {
                _notes.RemoveAt(index);
                removed = true;
            }
        }

        if (removed)
        {
            Save();
            NotesChanged?.Invoke();
            return true;
        }

        return false;
    }

    public NoteItem? Get(string id)
    {
        lock (_lock)
        {
            return _notes.FirstOrDefault(n => n.Id == id);
        }
    }
}
