using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DesktopPet.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> to %AppData%\Neku\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly string DefaultDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");

    private static readonly string DefaultFilePath = Path.Combine(DefaultDir, "settings.json");

    public static readonly string[] ObsoleteAiKeys = new[]
    {
        "aiApiKey",
        "aiProvider",
        "enableAiCompanion",
        "ollamaUrl",
        "ollamaModel",
        "aiStreaming",
        "aiModel",
        "aiPersona",
        "aiEnableTools",
        "aiProactiveChatter",
        "aiHotkeyEnabled",
        "aiHotkeyModifiers",
        "aiHotkeyKey",
        "aiEnableMemory"
    };

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly string _filePath;
    private readonly string _dir;

    public string SettingsPath => _filePath;

    private readonly object _lock = new();
    public string? LastCorruptBackupPath { get; private set; }
    public AppSettings Current { get; private set; } = new();

    public SettingsService(string? customFilePath = null)
    {
        _filePath = customFilePath ?? DefaultFilePath;
        _dir = Path.GetDirectoryName(_filePath) ?? DefaultDir;
    }

    /// <summary>
    /// Sanitizes raw JSON by stripping Neku-owned obsolete AI configuration keys,
    /// while strictly preserving all other user preferences and arbitrary non-AI fields.
    /// </summary>
    public static string SanitizeLegacyAiJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                foreach (var key in ObsoleteAiKeys)
                {
                    var propertyName = obj.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    if (propertyName != null)
                    {
                        obj.Remove(propertyName);
                    }
                }
                return obj.ToJsonString(Options);
            }
        }
        catch
        {
            // Non-fatal: return json untouched if parsing fails
        }
        return json;
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            bool migrated = false;
            string backupPath = _filePath + ".bak";

            // Check if primary file is missing but an orphaned temp file exists from an interrupted move
            if (!File.Exists(_filePath))
            {
                try
                {
                    string baseName = Path.GetFileName(_filePath);
                    if (Directory.Exists(_dir))
                    {
                        var candidates = Directory.GetFiles(_dir, baseName + ".tmp.*")
                                                  .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                                  .ToList();
                        if (candidates.Count > 0 && new FileInfo(candidates[0]).Length > 0)
                        {
                            File.Move(candidates[0], _filePath, overwrite: true);
                            DebugLog.Write($"[SettingsService] Recovered settings file from interrupted atomic write: {candidates[0]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[SettingsService] Error recovering temp file: {ex.Message}");
                }
            }

            try
            {
                // Sanitize existing backup file if present to eliminate obsolete credentials
                if (File.Exists(backupPath))
                {
                    try
                    {
                        string existingBak = File.ReadAllText(backupPath);
                        if (ObsoleteAiKeys.Any(key => existingBak.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase)))
                        {
                            string sanitizedBak = SanitizeLegacyAiJson(existingBak);
                            File.WriteAllText(backupPath, sanitizedBak);
                        }
                    }
                    catch { }
                }

                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);

                    // Detect if the file contains legacy AI configuration
                    bool hasLegacyAiKeys = ObsoleteAiKeys.Any(key => json.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase));

                    if (hasLegacyAiKeys)
                    {
                        // Create sanitized backup preserving other user preferences but removing obsolete AI credentials
                        if (!File.Exists(backupPath))
                        {
                            try
                            {
                                string sanitizedBak = SanitizeLegacyAiJson(json);
                                File.WriteAllText(backupPath, sanitizedBak);
                            }
                            catch { }
                        }
                        migrated = true;
                    }

                    Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[SettingsService] Deserialization failed: {ex.Message}. Backing up corrupted settings.");
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                        LastCorruptBackupPath = Path.Combine(_dir, $"settings.json.corrupt.{timeStamp}");
                        File.Copy(_filePath, LastCorruptBackupPath, overwrite: true);
                    }
                }
                catch { }

                Current = new AppSettings();
            }

            Current.Validate();

            // If legacy settings were found and migrated, write back the clean settings without obsolete AI keys
            if (migrated)
            {
                Save();
            }

            return Current;
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            string? tempPath = null;
            try
            {
                if (!string.IsNullOrEmpty(_dir))
                {
                    Directory.CreateDirectory(_dir);
                }
                string json = JsonSerializer.Serialize(Current, Options);
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
                // Non-fatal: settings just won't persist this session.
            }
        }
    }
}
