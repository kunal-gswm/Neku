using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.Agent;
using Xunit;

namespace DesktopPet.Tests;

public class LocalFirstSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public LocalFirstSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Neku_LocalFirstTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in tests
        }
    }

    [Fact]
    public void Assembly_DoesNotContain_AnyDirectAiTypes_OrAiProviders()
    {
        var assembly = typeof(AppSettings).Assembly;
        var types = assembly.GetTypes();

        var forbiddenTypeNames = new[]
        {
            "IAiProvider",
            "GeminiProvider",
            "OllamaProvider",
            "AiChatService",
            "AiMemory",
            "CuteTools",
            "EmotionMap",
            "ChatInputWindow"
        };

        foreach (var forbidden in forbiddenTypeNames)
        {
            Assert.DoesNotContain(types, t => t.Name == forbidden || t.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AppSettings_DoesNotContain_DirectAiProperties()
    {
        var properties = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name).ToList();

        var forbiddenProperties = new[]
        {
            "EnableAiCompanion",
            "AiProvider",
            "OllamaUrl",
            "OllamaModel",
            "AiStreaming",
            "AiApiKey",
            "AiModel",
            "AiPersona",
            "AiEnableTools",
            "AiProactiveChatter",
            "AiHotkeyEnabled",
            "AiHotkeyModifiers",
            "AiHotkeyKey",
            "AiEnableMemory"
        };

        foreach (var forbidden in forbiddenProperties)
        {
            Assert.DoesNotContain(forbidden, propertyNames);
        }
    }

    [Fact]
    public void SettingsService_MigratesLegacyAiSettings_AndCreatesBakBackup()
    {
        var settingsFile = Path.Combine(_tempDir, "settings.json");
        var bakFile = settingsFile + ".bak";

        var legacyJson = """
        {
          "activePet": "billu",
          "sizeScale": 1.75,
          "focusDurationMinutes": 45,
          "enableAiCompanion": true,
          "aiProvider": "Gemini",
          "aiApiKey": "super-secret-key-12345",
          "ollamaUrl": "http://localhost:11434",
          "ollamaModel": "llama3",
          "aiModel": "gemini-1.5-flash",
          "aiPersona": "helpful cat",
          "aiEnableTools": true,
          "aiProactiveChatter": true,
          "aiHotkeyEnabled": true,
          "aiHotkeyModifiers": 2,
          "aiHotkeyKey": 62,
          "aiEnableMemory": true,
          "enableAgentCompanion": true,
          "dailyFocusGoalMinutes": 120
        }
        """;

        File.WriteAllText(settingsFile, legacyJson);

        var service = new SettingsService(settingsFile);
        service.Load();

        // Verify loaded preferences
        Assert.Equal("billu", service.Current.ActivePet);
        Assert.Equal(1.75, service.Current.SizeScale);
        Assert.Equal(45, service.Current.FocusDurationMinutes);
        Assert.Equal(120, service.Current.DailyFocusGoalMinutes);
        Assert.True(service.Current.EnableAgentCompanion);

        // Verify .bak file created and sanitized of obsolete AI credentials
        Assert.True(File.Exists(bakFile), "A backup file with .bak extension must be created when legacy AI keys exist.");
        var bakContent = File.ReadAllText(bakFile);
        Assert.DoesNotContain("aiApiKey", bakContent);
        Assert.DoesNotContain("super-secret-key-12345", bakContent);
        Assert.DoesNotContain("aiProvider", bakContent);
        Assert.DoesNotContain("ollamaUrl", bakContent);
        Assert.DoesNotContain("ollamaModel", bakContent);
        Assert.DoesNotContain("aiModel", bakContent);
        Assert.DoesNotContain("aiPersona", bakContent);
        Assert.DoesNotContain("aiEnableTools", bakContent);
        Assert.DoesNotContain("aiProactiveChatter", bakContent);
        Assert.DoesNotContain("aiHotkeyEnabled", bakContent);
        Assert.DoesNotContain("aiHotkeyModifiers", bakContent);
        Assert.DoesNotContain("aiHotkeyKey", bakContent);
        Assert.DoesNotContain("aiEnableMemory", bakContent);
        Assert.DoesNotContain("enableAiCompanion", bakContent);

        // Verify .bak file preserves all unrelated user preferences
        Assert.Contains("billu", bakContent);
        Assert.Contains("1.75", bakContent);
        Assert.Contains("45", bakContent);
        Assert.Contains("120", bakContent);

        // Verify rewritten settings file has legacy AI keys stripped
        var cleanedContent = File.ReadAllText(settingsFile);
        Assert.DoesNotContain("aiApiKey", cleanedContent);
        Assert.DoesNotContain("super-secret-key-12345", cleanedContent);
        Assert.DoesNotContain("aiProvider", cleanedContent);
        Assert.DoesNotContain("ollamaUrl", cleanedContent);
        Assert.DoesNotContain("ollamaModel", cleanedContent);
        Assert.DoesNotContain("aiModel", cleanedContent);
        Assert.DoesNotContain("aiPersona", cleanedContent);
        Assert.DoesNotContain("aiEnableTools", cleanedContent);
        Assert.DoesNotContain("aiProactiveChatter", cleanedContent);
        Assert.DoesNotContain("aiHotkeyEnabled", cleanedContent);
        Assert.DoesNotContain("aiHotkeyModifiers", cleanedContent);
        Assert.DoesNotContain("aiHotkeyKey", cleanedContent);
        Assert.DoesNotContain("aiEnableMemory", cleanedContent);
        Assert.DoesNotContain("enableAiCompanion", cleanedContent);

        // Genuine settings must be preserved in rewritten file
        Assert.Contains("billu", cleanedContent);
        Assert.Contains("1.75", cleanedContent);
        Assert.Contains("45", cleanedContent);
    }

    [Fact]
    public void SettingsService_SanitizesExistingBakFile_OnLoad()
    {
        var settingsFile = Path.Combine(_tempDir, "existing_settings.json");
        var bakFile = settingsFile + ".bak";

        File.WriteAllText(settingsFile, """{"activePet": "billu"}""");
        File.WriteAllText(bakFile, """{"activePet": "billu", "aiApiKey": "secret-legacy-token-999"}""");

        var service = new SettingsService(settingsFile);
        service.Load();

        var bakContent = File.ReadAllText(bakFile);
        Assert.DoesNotContain("aiApiKey", bakContent);
        Assert.DoesNotContain("secret-legacy-token-999", bakContent);
        Assert.Contains("billu", bakContent);
    }

    [Fact]
    public void SettingsService_DoesNotCreateBak_WhenSettingsAreAlreadyClean()
    {
        var settingsFile = Path.Combine(_tempDir, "clean_settings.json");
        var bakFile = settingsFile + ".bak";

        var cleanJson = """
        {
          "activePet": "neku",
          "sizeScale": 1.0,
          "focusDurationMinutes": 25,
          "enableAgentCompanion": true
        }
        """;

        File.WriteAllText(settingsFile, cleanJson);

        var service = new SettingsService(settingsFile);
        service.Load();
        Assert.Equal("neku", service.Current.ActivePet);
        Assert.False(File.Exists(bakFile), "Clean settings must not generate a .bak backup file.");
    }

    [Fact]
    public void SettingsService_StartsCleanly_WithDefaultSettings_WhenFileDoesNotExist()
    {
        var nonExistentFile = Path.Combine(_tempDir, "non_existent.json");
        var service = new SettingsService(nonExistentFile);
        service.Load();

        Assert.NotNull(service.Current);
        Assert.Equal("neku", service.Current.ActivePet);
        Assert.False(File.Exists(nonExistentFile + ".bak"));
    }

    [Fact]
    public void LocalCodingAgentCompanion_FunctionsFully_WithoutAiInference()
    {
        var sessionsFile = Path.Combine(_tempDir, "agent_sessions.json");
        var service = new AgentActivityService(sessionsFile);
        AgentEvent? receivedEvent = null;
        service.EventReceived += (e) => receivedEvent = e;

        string payload = """
        {
            "session_id": "sess-local-test",
            "cwd": "D:\\Projects\\pixelpaws",
            "hook_event_name": "SessionStart"
        }
        """;

        var (success, msg, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.NotNull(receivedEvent);
        Assert.Equal("sess-local-test", evt.SessionId);
        Assert.Equal(AgentEventType.SessionStarted, evt.EventType);

        // Verify personality locally reacts to the event without any external AI call
        var personality = new Personality();
        var comment = personality.GetAgentStartedLine();

        Assert.False(string.IsNullOrWhiteSpace(comment));
    }
}
