using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopPet.Services.Agent;

/// <summary>
/// Result of a provider configuration check or application.
/// </summary>
public sealed class IntegrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? BackupPath { get; set; }
    public string? DiffText { get; set; }
}

/// <summary>
/// Safe manager for local Claude Code hook integration.
/// Discovers installation, generates diffs, backs up user settings,
/// and applies or cleanly rolls back Neku's hook entries.
/// </summary>
public sealed class ClaudeCodeIntegrationService
{
    public const string HookIdentifier = "Neku Activity Companion";
    private readonly string? _customSettingsPath;

    public ClaudeCodeIntegrationService(string? customSettingsPath = null)
    {
        _customSettingsPath = customSettingsPath;
    }

    /// <summary>
    /// Builds the resilient, non-blocking hook command for Windows.
    /// Uses -H without quotes, -d @- for stdin streaming, and -o NUL to ensure no decision output is produced.
    /// </summary>
    public static string BuildHookCommand(int port) =>
        $"cmd.exe /c \"curl.exe -s -m 1 -X POST http://127.0.0.1:{port}/api/agent-events -H Content-Type:application/json -d @- -o NUL || exit 0\"";

    /// <summary>
    /// Gets the resolved path to ~/.claude/settings.json.
    /// </summary>
    public string GetSettingsPath()
    {
        if (!string.IsNullOrWhiteSpace(_customSettingsPath))
            return _customSettingsPath;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", "settings.json");
    }

    /// <summary>
    /// Checks whether Claude Code is installed or detected on the local machine.
    /// </summary>
    public bool IsClaudeDetected(out string detectedPath)
    {
        // 1. Check if ~/.claude directory exists
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string claudeDir = Path.Combine(userProfile, ".claude");
        if (Directory.Exists(claudeDir))
        {
            detectedPath = claudeDir;
            return true;
        }

        // 2. Check ~/.local/bin/claude.exe
        string localBinClaude = Path.Combine(userProfile, ".local", "bin", "claude.exe");
        if (File.Exists(localBinClaude))
        {
            detectedPath = localBinClaude;
            return true;
        }

        // 3. Check AppData npm or local bin
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string npmClaude = Path.Combine(appData, "npm", "claude.cmd");
        if (File.Exists(npmClaude))
        {
            detectedPath = npmClaude;
            return true;
        }

        detectedPath = "";
        return false;
    }

    /// <summary>
    /// Checks whether Neku's hooks are currently installed in the target settings.json.
    /// </summary>
    public bool IsHookInstalled(string? settingsPath = null)
    {
        string path = settingsPath ?? GetSettingsPath();
        if (!File.Exists(path)) return false;

        try
        {
            string content = File.ReadAllText(path);
            var node = JsonNode.Parse(content);
            if (node is not JsonObject root) return false;

            if (root["hooks"] is not JsonObject hooksObj) return false;

            foreach (var kvp in hooksObj)
            {
                if (kvp.Value is JsonArray matchersArray)
                {
                    foreach (var matcherItem in matchersArray)
                    {
                        if (matcherItem is JsonObject matcherObj && matcherItem["hooks"] is JsonArray hookList)
                        {
                            foreach (var h in hookList)
                            {
                                if (IsNekuHookNode(h))
                                    return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Generates the proposed settings.json string for enabling or disabling Neku hooks.
    /// </summary>
    public string GenerateProposedJson(string currentContent, bool enable, int port = 48123)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(currentContent) ? "{}" : currentContent) as JsonObject ?? new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        if (enable)
        {
            // Ensure "hooks" exists
            if (root["hooks"] is not JsonObject hooksObj)
            {
                hooksObj = new JsonObject();
                root["hooks"] = hooksObj;
            }

            string[] eventsToHook =
            [
                "SessionStart",
                "PreToolUse",
                "PostToolUse",
                "PostToolUseFailure",
                "PermissionRequest",
                "Notification",
                "Stop",
                "SessionEnd"
            ];
            string hookCommand = BuildHookCommand(port);

            foreach (string ev in eventsToHook)
            {
                if (hooksObj[ev] is not JsonArray matchersArray)
                {
                    matchersArray = new JsonArray();
                    hooksObj[ev] = matchersArray;
                }

                // Check if we already have a matcher with Neku hook
                JsonObject? existingMatcher = null;
                JsonObject? existingHook = null;

                foreach (var m in matchersArray)
                {
                    if (m is JsonObject mObj && m["hooks"] is JsonArray hArray)
                    {
                        foreach (var h in hArray)
                        {
                            if (IsNekuHookNode(h))
                            {
                                existingMatcher = mObj;
                                existingHook = h as JsonObject;
                                break;
                            }
                        }
                    }
                    if (existingMatcher != null) break;
                }

                if (existingHook != null)
                {
                    // Update command and async flag in place
                    existingHook["command"] = hookCommand;
                    existingHook["async"] = true;
                }
                else
                {
                    // Create new matcher entry with async: true
                    var newHookEntry = new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = hookCommand,
                        ["async"] = true,
                        ["statusMessage"] = HookIdentifier
                    };

                    var newMatcher = new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray { newHookEntry }
                    };

                    matchersArray.Add(newMatcher);
                }
            }
        }
        else
        {
            // Uninstall: remove ONLY Neku's hooks, preserving everything else
            if (root["hooks"] is JsonObject hooksObj)
            {
                var eventsToRemove = new List<string>();

                foreach (var (eventName, eventValue) in hooksObj)
                {
                    if (eventValue is JsonArray matchersArray)
                    {
                        var matchersToRemove = new List<JsonNode>();

                        foreach (var matcherNode in matchersArray)
                        {
                            if (matcherNode is JsonObject matcherObj && matcherObj["hooks"] is JsonArray hArray)
                            {
                                var hooksToRemove = new List<JsonNode>();
                                foreach (var h in hArray)
                                {
                                    if (h != null && IsNekuHookNode(h))
                                        hooksToRemove.Add(h);
                                }

                                foreach (var h in hooksToRemove)
                                    hArray.Remove(h);

                                if (hArray.Count == 0)
                                    matchersToRemove.Add(matcherNode);
                            }
                        }

                        foreach (var m in matchersToRemove)
                            matchersArray.Remove(m);

                        if (matchersArray.Count == 0)
                            eventsToRemove.Add(eventName);
                    }
                }

                foreach (var ev in eventsToRemove)
                    hooksObj.Remove(ev);

                if (hooksObj.Count == 0)
                    root.Remove("hooks");
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return root.ToJsonString(options);
    }

    /// <summary>
    /// Generates a readable unified diff between original and proposed JSON.
    /// </summary>
    public string GenerateDiff(string originalContent, string proposedContent)
    {
        var origLines = (originalContent ?? "").Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var propLines = (proposedContent ?? "").Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        var sb = new StringBuilder();
        sb.AppendLine("--- Current ~/.claude/settings.json");
        sb.AppendLine("+++ Proposed ~/.claude/settings.json");

        // Basic LCS / line diff
        int max = Math.Max(origLines.Length, propLines.Length);
        int o = 0, p = 0;

        while (o < origLines.Length || p < propLines.Length)
        {
            if (o < origLines.Length && p < propLines.Length && origLines[o] == propLines[p])
            {
                sb.AppendLine($"  {origLines[o]}");
                o++;
                p++;
            }
            else if (o < origLines.Length && (p >= propLines.Length || !propLines.Contains(origLines[o])))
            {
                sb.AppendLine($"- {origLines[o]}");
                o++;
            }
            else if (p < propLines.Length)
            {
                sb.AppendLine($"+ {propLines[p]}");
                p++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Applies proposed hook configuration, taking a prior backup.
    /// </summary>
    public IntegrationResult Apply(bool enable, int port = 48123, string? settingsPath = null)
    {
        string path = settingsPath ?? GetSettingsPath();
        string currentContent = "{}";

        if (File.Exists(path))
        {
            try
            {
                currentContent = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return new IntegrationResult { Success = false, Message = $"Failed to read settings file: {ex.Message}" };
            }
        }
        else
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    return new IntegrationResult { Success = false, Message = $"Failed to create directory {dir}: {ex.Message}" };
                }
            }
        }

        string proposed = GenerateProposedJson(currentContent, enable, port);
        string diff = GenerateDiff(currentContent, proposed);

        // 1. Create backup if file existed
        string? backupPath = null;
        if (File.Exists(path))
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                backupPath = $"{path}.neku_backup_{timestamp}";
                File.Copy(path, backupPath, overwrite: true);
            }
            catch (Exception ex)
            {
                return new IntegrationResult { Success = false, Message = $"Failed to create backup: {ex.Message}" };
            }
        }

        // 2. Write proposed file
        try
        {
            File.WriteAllText(path, proposed, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return new IntegrationResult { Success = false, Message = $"Failed to write settings: {ex.Message}", BackupPath = backupPath };
        }

        return new IntegrationResult
        {
            Success = true,
            Message = enable
                ? "Claude Code hooks installed successfully. Neku will now receive activity notifications."
                : "Claude Code hooks uninstalled cleanly. Unrelated configuration was preserved.",
            BackupPath = backupPath,
            DiffText = diff
        };
    }

    /// <summary>
    /// Lists all existing Neku backups for the settings file.
    /// </summary>
    public IReadOnlyList<string> GetBackups(string? settingsPath = null)
    {
        string path = settingsPath ?? GetSettingsPath();
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];

        string pattern = $"{Path.GetFileName(path)}.neku_backup_*";
        return Directory.GetFiles(dir, pattern).OrderByDescending(f => f).ToList();
    }

    /// <summary>
    /// Restores a selected backup file.
    /// Automatically creates a pre-restore backup of the current target file before overwriting.
    /// </summary>
    public bool RestoreBackup(string backupPath, string? targetPath = null)
    {
        string target = targetPath ?? GetSettingsPath();
        if (!File.Exists(backupPath)) return false;

        try
        {
            if (File.Exists(target))
            {
                string safetyTimestamp = $"{DateTime.Now:yyyyMMdd_HHmmss}_prerestore";
                string safetyBackup = $"{target}.neku_backup_{safetyTimestamp}";
                File.Copy(target, safetyBackup, overwrite: true);
            }

            File.Copy(backupPath, target, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNekuHookNode(JsonNode? node)
    {
        if (node is not JsonObject hObj) return false;

        if (hObj["statusMessage"]?.ToString() == HookIdentifier)
            return true;

        string? cmd = hObj["command"]?.ToString();
        if (!string.IsNullOrEmpty(cmd) && (cmd.Contains("/api/agent-events") || cmd.Contains("/agent-events")))
            return true;

        return false;
    }
}
