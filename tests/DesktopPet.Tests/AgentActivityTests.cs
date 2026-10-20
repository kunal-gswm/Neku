using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.Agent;
using Xunit;

namespace DesktopPet.Tests;

public class AgentActivityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testSessionsFile;
    private readonly string _testClaudeSettingsFile;

    public AgentActivityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neku_agent_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testSessionsFile = Path.Combine(_tempDir, "agent_sessions.json");
        _testClaudeSettingsFile = Path.Combine(_tempDir, "claude_settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void ClaudeCode_SessionStart_NormalizesCorrectly()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-123",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "SessionStart"
        }
        """;

        var (success, msg, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal("session-123", evt.SessionId);
        Assert.Equal("TestApp", evt.ProjectName);
        Assert.Equal(AgentEventType.SessionStarted, evt.EventType);
        Assert.Equal(AgentEventStatus.Normal, evt.Status);
        Assert.Contains("Session started", evt.Summary);
    }

    [Fact]
    public void ClaudeCode_PreToolUse_WithEditTool_ExtractsFilePath()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-456",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PreToolUse",
            "tool_name": "Edit",
            "tool_input": {
                "file_path": "src/App.xaml.cs",
                "old_string": "foo",
                "new_string": "bar"
            }
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal("session-456", evt.SessionId);
        Assert.Equal(AgentEventType.ToolWorking, evt.EventType);
        Assert.Equal("Edit", evt.ToolName);
        Assert.Equal("src/App.xaml.cs", evt.FilePath);
        Assert.Contains("App.xaml.cs", evt.Summary);
    }

    [Fact]
    public void ClaudeCode_PostToolUse_WithEdit_NormalizesToFileChanged()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-456",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PostToolUse",
            "tool_name": "Edit",
            "tool_input": {
                "file_path": "src/App.xaml.cs"
            }
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.FileChanged, evt.EventType);
        Assert.Contains("Modified App.xaml.cs", evt.Summary);
    }

    [Fact]
    public void ClaudeCode_Notification_NormalizesToWaitingForInput()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-789",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "Notification",
            "message": "Allow execution of bash script?"
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.WaitingForInput, evt.EventType);
        Assert.Equal(AgentEventStatus.Waiting, evt.Status);
        Assert.True(evt.RequiresApproval);

        var session = service.GetSession("session-789");
        Assert.NotNull(session);
        Assert.Equal(AgentSessionStatus.WaitingForInput, session.Status);
    }

    [Fact]
    public void ClaudeCode_Stop_NormalizesToTaskCompleted()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-999",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "Stop"
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.TaskCompleted, evt.EventType);
        Assert.Equal(AgentEventStatus.Success, evt.Status);

        var session = service.GetSession("session-999");
        Assert.NotNull(session);
        Assert.Equal(AgentSessionStatus.Completed, session.Status);
    }

    [Fact]
    public void GenericPayload_NormalizesSuccessfully()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "provider_id": "aider",
            "provider_name": "Aider",
            "session_id": "aider-1",
            "project_name": "MyProject",
            "event_type": "file_changed",
            "summary": "Updated README.md",
            "file_path": "README.md",
            "status": "normal"
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal("aider", evt.ProviderId);
        Assert.Equal("Aider", evt.ProviderName);
        Assert.Equal("aider-1", evt.SessionId);
        Assert.Equal("MyProject", evt.ProjectName);
        Assert.Equal(AgentEventType.FileChanged, evt.EventType);
        Assert.Equal("README.md", evt.FilePath);
    }

    [Fact]
    public void IngestRawJson_RejectsPayloadsOver64KB()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string hugeMessage = new string('x', 70000);
        string payload = $"{{\"session_id\":\"s1\",\"hook_event_name\":\"Stop\",\"message\":\"{hugeMessage}\"}}";

        var (success, msg, evt) = service.IngestRawJson(payload);

        Assert.False(success);
        Assert.Null(evt);
        Assert.Contains("64KB", msg);
    }

    [Fact]
    public void IngestRawJson_HandlesMalformedJsonWithoutCrashing()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string malformed = "{ this is not valid json }";

        var (success, msg, evt) = service.IngestRawJson(malformed);

        Assert.False(success);
        Assert.Null(evt);
        Assert.Contains("Malformed", msg);
    }

    [Fact]
    public void Deduplication_DropsIdenticalEventsWithin300ms()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-dedup",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PreToolUse",
            "tool_name": "Bash"
        }
        """;

        var res1 = service.IngestRawJson(payload);
        var res2 = service.IngestRawJson(payload);

        Assert.True(res1.Success);
        Assert.False(res2.Success); // Dropped as duplicate
        Assert.Contains("Duplicate", res2.Message);

        var session = service.GetSession("session-dedup");
        Assert.NotNull(session);
        Assert.Single(session.Events);
    }

    [Fact]
    public void MultipleSessions_TrackConcurrentlyAndIndependently()
    {
        var service = new AgentActivityService(_testSessionsFile);

        service.IngestRawJson("""{"session_id":"sess-A","cwd":"D:\\A","hook_event_name":"SessionStart"}""");
        service.IngestRawJson("""{"session_id":"sess-B","cwd":"D:\\B","hook_event_name":"SessionStart"}""");

        service.IngestRawJson("""{"session_id":"sess-A","cwd":"D:\\A","hook_event_name":"Stop"}""");

        var sessA = service.GetSession("sess-A");
        var sessB = service.GetSession("sess-B");

        Assert.NotNull(sessA);
        Assert.NotNull(sessB);
        Assert.Equal(AgentSessionStatus.Completed, sessA.Status);
        Assert.Equal(AgentSessionStatus.Active, sessB.Status);
    }

    [Fact]
    public void SessionFilter_CorrectlyFiltersActiveWaitingAndCompleted()
    {
        var service = new AgentActivityService(_testSessionsFile);

        service.IngestRawJson("""{"session_id":"s-active","cwd":"D:\\A","hook_event_name":"SessionStart"}""");
        service.IngestRawJson("""{"session_id":"s-waiting","cwd":"D:\\B","hook_event_name":"Notification"}""");
        service.IngestRawJson("""{"session_id":"s-completed","cwd":"D:\\C","hook_event_name":"Stop"}""");

        var all = service.GetSessions("all");
        var active = service.GetSessions("active");
        var waiting = service.GetSessions("waiting");
        var completed = service.GetSessions("completed");

        Assert.Equal(3, all.Count);
        Assert.Single(active);
        Assert.Equal("s-active", active[0].SessionId);
        Assert.Single(waiting);
        Assert.Equal("s-waiting", waiting[0].SessionId);
        Assert.Single(completed);
        Assert.Equal("s-completed", completed[0].SessionId);
    }

    [Fact]
    public void ClearCompletedHistory_RemovesFinishedSessionsPreservesActive()
    {
        var service = new AgentActivityService(_testSessionsFile);

        service.IngestRawJson("""{"session_id":"s-active","cwd":"D:\\A","hook_event_name":"SessionStart"}""");
        service.IngestRawJson("""{"session_id":"s-done","cwd":"D:\\B","hook_event_name":"Stop"}""");

        int cleared = service.ClearCompletedHistory();

        Assert.Equal(1, cleared);
        Assert.NotNull(service.GetSession("s-active"));
        Assert.Null(service.GetSession("s-done"));
    }

    [Fact]
    public void BoundedRetention_EnforcesMaxSessionsAndMaxEvents()
    {
        var service = new AgentActivityService(_testSessionsFile)
        {
            MaxSessionsRetained = 3,
            MaxEventsPerSession = 4
        };

        // Add 6 events to a single session with distinct tools
        for (int i = 0; i < 6; i++)
        {
            service.RecordEvent(new AgentEvent
            {
                SessionId = "overflow-sess",
                EventType = AgentEventType.ToolWorking,
                ToolName = $"tool_{i}",
                Summary = $"Step {i}",
                Timestamp = DateTime.Now.AddSeconds(i)
            });
        }

        var session = service.GetSession("overflow-sess");
        Assert.NotNull(session);
        Assert.Equal(4, session.Events.Count); // Enforced to max 4

        // Add 5 sessions
        for (int s = 0; s < 5; s++)
        {
            service.RecordEvent(new AgentEvent
            {
                SessionId = $"sess-{s}",
                EventType = AgentEventType.TaskCompleted,
                Summary = "Done",
                Timestamp = DateTime.Now.AddMinutes(s)
            });
        }

        var sessions = service.GetSessions("all");
        Assert.True(sessions.Count <= 3); // Enforced to max 3
    }

    [Fact]
    public void Persistence_SavesAndReloadsAcrossInstances()
    {
        var service1 = new AgentActivityService(_testSessionsFile);
        service1.IngestRawJson("""{"session_id":"persisted-1","cwd":"D:\\Work","hook_event_name":"SessionStart"}""");

        var service2 = new AgentActivityService(_testSessionsFile);
        var loadedSession = service2.GetSession("persisted-1");

        Assert.NotNull(loadedSession);
        Assert.Equal("persisted-1", loadedSession.SessionId);
        Assert.Equal("Work", loadedSession.ProjectName);
    }

    [Fact]
    public void ClaudeCodeIntegration_GenerateDiffAndApply_PreservesUserConfig()
    {
        string initialConfig = """
        {
            "env": {
                "ANTHROPIC_BASE_URL": "https://api.example.com",
                "MY_SECRET": "preserve_this"
            },
            "theme": "dark"
        }
        """;
        File.WriteAllText(_testClaudeSettingsFile, initialConfig);

        var installer = new ClaudeCodeIntegrationService(_testClaudeSettingsFile);

        // 1. Verify not installed initially
        Assert.False(installer.IsHookInstalled());

        // 2. Generate diff
        string proposed = installer.GenerateProposedJson(initialConfig, enable: true, port: 48123);
        string diff = installer.GenerateDiff(initialConfig, proposed);
        Assert.Contains("+", diff);
        Assert.Contains("hooks", proposed);

        // 3. Apply hooks
        var result = installer.Apply(enable: true, port: 48123);
        Assert.True(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(installer.IsHookInstalled());

        // 4. Verify original user keys were preserved untouched!
        string modifiedJson = File.ReadAllText(_testClaudeSettingsFile);
        var root = JsonNode.Parse(modifiedJson) as JsonObject;
        Assert.NotNull(root);
        Assert.Equal("dark", root["theme"]?.ToString());
        Assert.Equal("preserve_this", root["env"]?["MY_SECRET"]?.ToString());

        // 5. Clean uninstall
        var uninstallResult = installer.Apply(enable: false, port: 48123);
        Assert.True(uninstallResult.Success);
        Assert.False(installer.IsHookInstalled());

        // Verify user keys still preserved, and "hooks" object cleanly removed
        string uninstalledJson = File.ReadAllText(_testClaudeSettingsFile);
        var uninstalledRoot = JsonNode.Parse(uninstalledJson) as JsonObject;
        Assert.NotNull(uninstalledRoot);
        Assert.Equal("dark", uninstalledRoot["theme"]?.ToString());
        Assert.Equal("preserve_this", uninstalledRoot["env"]?["MY_SECRET"]?.ToString());
        Assert.Null(uninstalledRoot["hooks"]);
    }

    [Fact]
    public void Personality_HasDistinctAgentLinesForNekuAndBillu()
    {
        var personality = new Personality();

        // Neku lines
        personality.SetActivePet("neku");
        string nekuStart = personality.GetAgentStartedLine();
        string nekuWait = personality.GetAgentWaitingLine();
        string nekuDone = personality.GetAgentCompletedLine();
        string nekuFail = personality.GetAgentFailedLine();
        string nekuIdle = personality.GetAgentIdleLine();

        Assert.False(string.IsNullOrWhiteSpace(nekuStart));
        Assert.False(string.IsNullOrWhiteSpace(nekuWait));
        Assert.False(string.IsNullOrWhiteSpace(nekuDone));
        Assert.False(string.IsNullOrWhiteSpace(nekuFail));
        Assert.False(string.IsNullOrWhiteSpace(nekuIdle));

        // Billu lines
        personality.SetActivePet("billu");
        string billuStart = personality.GetAgentStartedLine();
        string billuWait = personality.GetAgentWaitingLine();
        string billuDone = personality.GetAgentCompletedLine();
        string billuFail = personality.GetAgentFailedLine();
        string billuIdle = personality.GetAgentIdleLine();

        Assert.False(string.IsNullOrWhiteSpace(billuStart));
        Assert.False(string.IsNullOrWhiteSpace(billuWait));
        Assert.False(string.IsNullOrWhiteSpace(billuDone));
        Assert.False(string.IsNullOrWhiteSpace(billuFail));
        Assert.False(string.IsNullOrWhiteSpace(billuIdle));

        // Billu personality uses street-smart/dry humor (e.g. Claude cooking / bro)
        Assert.True(billuStart.Contains("Claude", StringComparison.OrdinalIgnoreCase) ||
                    billuStart.Contains("bro", StringComparison.OrdinalIgnoreCase) ||
                    billuStart.Contains("agent", StringComparison.OrdinalIgnoreCase) ||
                    billuStart.Contains("loaf", StringComparison.OrdinalIgnoreCase) ||
                    billuStart.Contains("AI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RedactPath_SanitizesUserProfileDirectory()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string secretPath = Path.Combine(userProfile, "projects", "secret-agent", "file.cs");

        string redacted = AgentEvent.RedactPath(secretPath);

        Assert.StartsWith("~", redacted);
        Assert.DoesNotContain(userProfile, redacted);
    }

    [Fact]
    public void ClaudeCode_PostToolUseFailure_NormalizesToTaskFailed()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-fail",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PostToolUseFailure",
            "tool_name": "Bash",
            "error": "Exit status 1: compiler error in Program.cs"
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.TaskFailed, evt.EventType);
        Assert.Equal(AgentEventStatus.Failure, evt.Status);
        Assert.Contains("Tool failed", evt.Summary);
        Assert.Contains("Bash", evt.Summary);

        var session = service.GetSession("session-fail");
        Assert.NotNull(session);
        Assert.Equal(AgentSessionStatus.Failed, session.Status);
    }

    [Fact]
    public void ClaudeCode_PermissionRequest_NormalizesToWaitingForInput_RequiresApproval()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-perm",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PermissionRequest",
            "tool_name": "Bash",
            "tool_input": {
                "command": "git push origin main --force"
            }
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.WaitingForInput, evt.EventType);
        Assert.Equal(AgentEventStatus.Waiting, evt.Status);
        Assert.True(evt.RequiresApproval);
        Assert.Contains("Waiting for permission", evt.Summary);
        Assert.Contains("Bash", evt.Summary);

        var session = service.GetSession("session-perm");
        Assert.NotNull(session);
        Assert.Equal(AgentSessionStatus.WaitingForInput, session.Status);
    }

    [Fact]
    public void ClaudeCode_SessionEnd_NormalizesToSessionEnded()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string payload = """
        {
            "session_id": "session-end-test",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "SessionEnd"
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal(AgentEventType.SessionEnded, evt.EventType);

        var session = service.GetSession("session-end-test");
        Assert.NotNull(session);
        Assert.Equal(AgentSessionStatus.Completed, session.Status);
    }

    [Fact]
    public void ClaudeCodeIntegration_GeneratesAllEightEventsWithAsyncFlag()
    {
        var installer = new ClaudeCodeIntegrationService(_testClaudeSettingsFile);
        string json = installer.GenerateProposedJson("{}", enable: true, port: 48123);

        var root = JsonNode.Parse(json) as JsonObject;
        Assert.NotNull(root);
        var hooks = root["hooks"] as JsonObject;
        Assert.NotNull(hooks);

        string[] expectedEvents =
        [
            "SessionStart", "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionRequest", "Notification", "Stop", "SessionEnd"
        ];

        foreach (string ev in expectedEvents)
        {
            var matchers = hooks[ev] as JsonArray;
            Assert.NotNull(matchers);
            Assert.NotEmpty(matchers);

            var firstMatcher = matchers[0] as JsonObject;
            Assert.NotNull(firstMatcher);
            var innerHooks = firstMatcher["hooks"] as JsonArray;
            Assert.NotNull(innerHooks);
            Assert.NotEmpty(innerHooks);

            var hookEntry = innerHooks[0] as JsonObject;
            Assert.NotNull(hookEntry);
            Assert.Equal(true, hookEntry["async"]?.GetValue<bool>());
            Assert.Contains("-o NUL", hookEntry["command"]?.ToString());
            Assert.Contains("|| exit 0", hookEntry["command"]?.ToString());
        }
    }

    [Fact]
    public void PrivacyBoundary_DoesNotPersistSecretsOrRawSourceCode()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string secretApiKey = "sk-proj-1234567890abcdefghijklmnopqrstuvwxyz1234567890";
        string secretGhToken = "ghp_1234567890abcdefghijklmnopqrstuvwxyz";
        string secretBearer = "Bearer secret_bearer_token_xyz_123456789";
        string secretPassword = "SuperSecretPassword123!";
        string fakeSourceCode = "public class ClassifiedBankVault { private string _vaultCode = 'SECRET123'; }";

        string payload = $$"""
        {
            "session_id": "privacy-session-1",
            "cwd": "D:\\Projects\\TestApp",
            "hook_event_name": "PreToolUse",
            "tool_name": "Edit",
            "tool_input": {
                "file_path": "src/Vault.cs",
                "file_text": "{{fakeSourceCode}}",
                "command": "curl -H \"Authorization: {{secretBearer}}\" https://api.vault.com?key={{secretApiKey}}&password={{secretPassword}}",
                "gh_token": "{{secretGhToken}}"
            },
            "env": {
                "DATABASE_URL": "postgres://user:{{secretPassword}}@localhost/db"
            }
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);
        Assert.True(success);
        Assert.NotNull(evt);

        // 1. In memory verification
        Assert.DoesNotContain(secretApiKey, evt.Summary);
        Assert.DoesNotContain(secretGhToken, evt.Summary);
        Assert.DoesNotContain(secretBearer, evt.Summary);
        Assert.DoesNotContain(secretPassword, evt.Summary);
        Assert.DoesNotContain(fakeSourceCode, evt.Summary);

        // 2. On disk verification: agent_sessions.json must NEVER contain secret strings or raw source code
        Assert.True(File.Exists(_testSessionsFile));
        string savedJson = File.ReadAllText(_testSessionsFile);

        Assert.DoesNotContain(secretApiKey, savedJson);
        Assert.DoesNotContain(secretGhToken, savedJson);
        Assert.DoesNotContain(secretBearer, savedJson);
        Assert.DoesNotContain(secretPassword, savedJson);
        Assert.DoesNotContain(fakeSourceCode, savedJson);
        Assert.DoesNotContain("ClassifiedBankVault", savedJson);
        Assert.DoesNotContain("DATABASE_URL", savedJson);
    }

    [Fact]
    public void PrivacyBoundary_RedactsUserProfileOnDiskAndResolves()
    {
        var service = new AgentActivityService(_testSessionsFile);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string workingDir = Path.Combine(userProfile, "code", "secret-agent-project");
        string filePath = Path.Combine(workingDir, "src", "Main.cs");

        string payload = $$"""
        {
            "session_id": "redact-session",
            "cwd": "{{workingDir.Replace("\\", "\\\\")}}",
            "hook_event_name": "PreToolUse",
            "tool_name": "Edit",
            "tool_input": {
                "file_path": "{{filePath.Replace("\\", "\\\\")}}"
            }
        }
        """;

        var (success, _, evt) = service.IngestRawJson(payload);
        Assert.True(success);
        Assert.NotNull(evt);

        // Event path is redacted
        Assert.StartsWith("~", evt.WorkingDirectory);
        Assert.DoesNotContain(userProfile, evt.WorkingDirectory);

        // Disk persistence must not contain raw user profile path
        string diskJson = File.ReadAllText(_testSessionsFile);
        Assert.DoesNotContain(userProfile, diskJson);

        // Resolving ~ restores the real path for launching Explorer
        string resolvedDir = AgentSession.ResolvePath(evt.WorkingDirectory);
        Assert.Equal(workingDir, resolvedDir);
    }

    [Fact]
    public void RequestRateLimiter_AllowsUpToLimit_RejectsExcess()
    {
        var limiter = new RequestRateLimiter(maxRequests: 5, window: TimeSpan.FromSeconds(1));

        for (int i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire(), $"Request {i + 1} should be accepted");
        }

        // 6th request exceeds limit
        Assert.False(limiter.TryAcquire(), "Request 6 should be rejected by rate limiter");

        // Wait for window to slide
        Thread.Sleep(1100);

        Assert.True(limiter.TryAcquire(), "Request after window expiration should be accepted");
    }

    [Fact]
    public async Task DeveloperEventService_RateLimitAndPayloadValidation()
    {
        int testPort = 48199;
        var settings = new AppSettings
        {
            EnableAgentCompanion = true,
            DeveloperPort = testPort
        };
        var activityService = new AgentActivityService(_testSessionsFile);

        using var devService = new DeveloperEventService(null, settings, Dispatcher.CurrentDispatcher, activityService);
        devService.Start();

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{testPort}/") };

            // 1. Normal payload succeeds
            var res = await http.PostAsync("/api/agent-events",
                new StringContent("{\"session_id\":\"s-e2e\",\"hook_event_name\":\"SessionStart\",\"cwd\":\"D:\\\\Test\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            // 2. Oversized payload returns 413
            string huge = new string('a', 70000);
            var hugeRes = await http.PostAsync("/api/agent-events",
                new StringContent(huge, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, hugeRes.StatusCode);

            // 3. Exhaust rate limiter
            // RateLimiter limit is 60; we already made 2 requests (normal + oversized), let's make 58 more to reach 60
            for (int i = 0; i < 58; i++)
            {
                var r = await http.GetAsync("/?type=build_success");
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            }

            // 61st request triggers rate limiter (429)
            var rateLimitedRes = await http.GetAsync("/?type=build_success");
            Assert.Equal((HttpStatusCode)429, rateLimitedRes.StatusCode);
            Assert.True(rateLimitedRes.Headers.Contains("Retry-After"));
        }
        finally
        {
            devService.Stop();
        }
    }

    [Fact]
    public async Task AsyncObservationHook_ProcessExecution_WorksWithLiveEndpointAndClosedEndpoint()
    {
        int testPort = 48198;
        string hookCmd = ClaudeCodeIntegrationService.BuildHookCommand(testPort);

        // Case 1: Endpoint is CLOSED (Neku is not running)
        // Command should run, fail to connect silently, and exit with code 0 in < 2.5 seconds
        var psiClosed = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{hookCmd}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var sw = Stopwatch.StartNew();
        using (var proc = Process.Start(psiClosed)!)
        {
            proc.StandardInput.WriteLine("{\"session_id\":\"sess-closed\",\"hook_event_name\":\"SessionStart\"}");
            proc.StandardInput.Close();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            await Task.WhenAll(outTask, errTask);
            bool exited = proc.WaitForExit(3500);
            string output = await outTask;

            sw.Stop();
            Assert.True(exited, "Process should exit within timeout");
            Assert.Equal(0, proc.ExitCode);
            Assert.Empty(output); // No output emitted
            Assert.True(sw.ElapsedMilliseconds < 3000, $"Process should finish quickly (took {sw.ElapsedMilliseconds}ms)");
        }

        // Case 2: Endpoint is OPEN (Neku is listening)
        var settings = new AppSettings
        {
            EnableAgentCompanion = true,
            DeveloperPort = testPort
        };
        var activityService = new AgentActivityService(_testSessionsFile);
        using var devService = new DeveloperEventService(null, settings, Dispatcher.CurrentDispatcher, activityService);
        devService.Start();
        Assert.True(devService.IsRunning, "DeveloperEventService should be active");

        // Warm up and verify HTTP listener is accepting connections
        using (var pingClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    var ping = await pingClient.GetAsync($"http://127.0.0.1:{testPort}/");
                    if (ping.StatusCode == HttpStatusCode.OK) break;
                }
                catch
                {
                    await Task.Delay(50);
                }
            }
        }

        try
        {
            var psiOpen = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{hookCmd}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            async Task RunLiveHookAsync()
            {
                using var proc = Process.Start(psiOpen)!;
                proc.StandardInput.WriteLine("{\"session_id\":\"sess-live-hook\",\"cwd\":\"D:\\\\TestLive\",\"hook_event_name\":\"SessionStart\"}");
                proc.StandardInput.Close();
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                await Task.WhenAll(outTask, errTask);
                bool exited = proc.WaitForExit(3500);
                string output = await outTask;

                Assert.True(exited);
                Assert.Equal(0, proc.ExitCode);
                Assert.Empty(output); // Output discarded to NUL, no permission decisions
            }

            await RunLiveHookAsync();

            // Verify event reached AgentActivityService with resilient polling
            AgentSession? session = null;
            for (int i = 0; i < 30; i++)
            {
                session = activityService.GetSession("sess-live-hook");
                if (session != null) break;
                await Task.Delay(50);
            }

            if (session == null)
            {
                // Retry once if first subprocess run raced with runner CPU contention
                await RunLiveHookAsync();
                for (int i = 0; i < 30; i++)
                {
                    session = activityService.GetSession("sess-live-hook");
                    if (session != null) break;
                    await Task.Delay(50);
                }
            }

            Assert.NotNull(session);
            Assert.Equal("sess-live-hook", session.SessionId);
            Assert.Equal(AgentSessionStatus.Active, session.Status);
        }
        finally
        {
            devService.Stop();
        }
    }

    [Fact]
    public void ClaudeCodeIntegration_InstallWithSpacesAndSpecialChars_AndRollbackSafety()
    {
        string specialDir = Path.Combine(_tempDir, "Special (Dir) & Spaces #1");
        Directory.CreateDirectory(specialDir);
        string specialSettings = Path.Combine(specialDir, "settings.json");

        File.WriteAllText(specialSettings, "{\n  \"theme\": \"system\",\n  \"customKey\": \"val1\"\n}");

        var installer = new ClaudeCodeIntegrationService(specialSettings);

        // 1. Install
        var applyRes = installer.Apply(enable: true, port: 48123);
        Assert.True(applyRes.Success);
        Assert.True(File.Exists(applyRes.BackupPath));

        // 2. User edits file after install
        string installedJson = File.ReadAllText(specialSettings);
        var root = JsonNode.Parse(installedJson) as JsonObject;
        Assert.NotNull(root);
        root["userAddedKey"] = "must_not_be_lost";
        File.WriteAllText(specialSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // 3. Uninstall
        var uninstallRes = installer.Apply(enable: false, port: 48123);
        Assert.True(uninstallRes.Success);

        // Verify user additions preserved
        string afterUninstall = File.ReadAllText(specialSettings);
        var afterRoot = JsonNode.Parse(afterUninstall) as JsonObject;
        Assert.NotNull(afterRoot);
        Assert.Equal("must_not_be_lost", afterRoot["userAddedKey"]?.ToString());
        Assert.Equal("val1", afterRoot["customKey"]?.ToString());
        Assert.Null(afterRoot["hooks"]);

        // 4. Test RestoreBackup safety: creates a pre-restore backup before restoring
        Assert.True(installer.RestoreBackup(applyRes.BackupPath!));
        var backups = installer.GetBackups();
        Assert.Contains(backups, b => b.Contains("prerestore"));
    }
}
