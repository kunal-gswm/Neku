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
        bool migrated = false;
        string backupPath = _filePath + ".bak";

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
        catch
        {
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

    public void Save()
    {
        try
        {
            if (!string.IsNullOrEmpty(_dir))
            {
                Directory.CreateDirectory(_dir);
            }
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, Options));
        }
        catch
        {
            // Non-fatal: settings just won't persist this session.
        }
    }
}
