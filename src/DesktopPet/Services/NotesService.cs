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
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IReadOnlyList<NoteItem> Notes => _notes;
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
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<NoteItem>>(json, JsonOpts);
                _notes.Clear();
                if (items != null)
                {
                    _notes.AddRange(items.OrderByDescending(n => n.UpdatedAt));
                }
            }
        }
        catch
        {
            // Fallback gracefully to empty notes list on read error
            _notes.Clear();
        }
    }

    public void Save()
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

        _notes.Insert(0, note);
        Save();
        NotesChanged?.Invoke();
        return note;
    }

    public void Update(string id, string title, string content)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note != null)
        {
            note.Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
            note.Content = content;
            note.UpdatedAt = DateTime.UtcNow;
            Save();
            NotesChanged?.Invoke();
        }
    }

    public bool Delete(string id)
    {
        int index = _notes.FindIndex(n => n.Id == id);
        if (index >= 0)
        {
            _notes.RemoveAt(index);
            Save();
            NotesChanged?.Invoke();
            return true;
        }
        return false;
    }

    public NoteItem? Get(string id) => _notes.FirstOrDefault(n => n.Id == id);
}
