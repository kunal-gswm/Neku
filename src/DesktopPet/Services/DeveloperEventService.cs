using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services.Agent;
using DesktopPet.Services.Radar;

namespace DesktopPet.Services;

public sealed class DeveloperEventPayload
{
    public string Type { get; set; } = "build_success";
    public string? Message { get; set; }
}

/// <summary>
/// Thread-safe sliding-window rate limiter with bounded memory.
/// Protects local developer endpoints against runaway loops or malicious bursts.
/// </summary>
public sealed class RequestRateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly Queue<long> _timestamps = new();
    private readonly object _lock = new();

    public RequestRateLimiter(int maxRequests = 60, TimeSpan? window = null)
    {
        _maxRequests = maxRequests;
        _window = window ?? TimeSpan.FromSeconds(10);
    }

    public bool TryAcquire()
    {
        lock (_lock)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long cutoffTicks = nowTicks - _window.Ticks;

            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoffTicks)
            {
                _timestamps.Dequeue();
            }

            if (_timestamps.Count >= _maxRequests)
            {
                return false;
            }

            _timestamps.Enqueue(nowTicks);
            return true;
        }
    }

    public int CurrentCount
    {
        get
        {
            lock (_lock)
            {
                long cutoffTicks = DateTime.UtcNow.Ticks - _window.Ticks;
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoffTicks)
                {
                    _timestamps.Dequeue();
                }
                return _timestamps.Count;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _timestamps.Clear();
        }
    }
}

/// <summary>
/// Lightweight local endpoint for developer events (builds, tests, commands)
/// and AI coding agent activity (Claude Code hooks, custom agent runners).
/// Completely disabled by default — zero threads, zero network sockets when off.
/// </summary>
public sealed class DeveloperEventService : IDisposable
{
    private HttpListener? _listener;
    private readonly PetEngine? _engine;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly AgentActivityService? _agentActivityService;
    private bool _running;

    public RequestRateLimiter RateLimiter { get; } = new(maxRequests: 60, window: TimeSpan.FromSeconds(10));
    public event Action<string, BuildTestResultStatus, string>? BuildTestEventReceived;

    public DeveloperEventService(PetEngine? engine, AppSettings settings, Dispatcher dispatcher, AgentActivityService? agentActivityService = null)
    {
        _engine = engine;
        _settings = settings;
        _dispatcher = dispatcher;
        _agentActivityService = agentActivityService;
    }

    public void Start()
    {
        bool shouldRun = _settings.EnableDeveloperEvents || _settings.EnableAgentCompanion;
        if (!shouldRun || _running) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_settings.DeveloperPort}/");
            _listener.Start();
            _running = true;
            _listener.BeginGetContext(OnContextReceived, null);
            DebugLog.Write($"DeveloperEventService started on port {_settings.DeveloperPort}.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Failed to start DeveloperEventService: {ex.Message}");
            _running = false;
            _listener = null;
        }
    }

    public void Stop()
    {
        _running = false;
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
            _listener = null;
            DebugLog.Write("DeveloperEventService stopped.");
        }
    }

    private void OnContextReceived(IAsyncResult ar)
    {
        if (!_running || _listener == null) return;

        try
        {
            var ctx = _listener.EndGetContext(ar);
            // Queue next request listener immediately
            _listener.BeginGetContext(OnContextReceived, null);

            // 1. Loopback restriction: strictly reject non-local traffic
            if (!ctx.Request.IsLocal && !IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address))
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                byte[] forbiddenBytes = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Forbidden: Endpoint restricted to loopback interface (127.0.0.1)\"}\n");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(forbiddenBytes, 0, forbiddenBytes.Length);
                ctx.Response.Close();
                return;
            }

            // 2. Sliding-window rate limit check
            if (!RateLimiter.TryAcquire())
            {
                ctx.Response.StatusCode = 429; // Too Many Requests
                ctx.Response.Headers.Add("Retry-After", "5");
                byte[] rateBytes = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Rate limit exceeded. Too many requests. Retry after 5 seconds.\"}\n");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(rateBytes, 0, rateBytes.Length);
                ctx.Response.Close();
                return;
            }

            // 3. Payload size validation (max 64 KB)
            if (ctx.Request.ContentLength64 > AgentActivityService.MaxPayloadBytes)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                byte[] errBytes = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"Payload exceeds 64KB limit\"}\n");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(errBytes, 0, errBytes.Length);
                ctx.Response.Close();
                return;
            }

            string path = ctx.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "/";
            bool isAgentRoute = path.StartsWith("/api/agent-events") ||
                                path.StartsWith("/agent-events") ||
                                path.StartsWith("/agent/events");

            if (ctx.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                string body = reader.ReadToEnd();

                // If explicitly on agent route or payload contains agent hook fields
                bool hasAgentSignature = isAgentRoute ||
                                         body.Contains("hook_event_name", StringComparison.OrdinalIgnoreCase) ||
                                         body.Contains("provider_id", StringComparison.OrdinalIgnoreCase);

                if (hasAgentSignature && _agentActivityService != null)
                {
                    var (success, msg, evt) = _agentActivityService.IngestRawJson(body);

                    ctx.Response.ContentType = "application/json";
                    ctx.Response.StatusCode = (int)HttpStatusCode.OK;

                    string responseJson = success
                        ? $"{{\"ok\":true,\"session_id\":\"{evt?.SessionId ?? ""}\",\"message\":\"{msg}\"}}\n"
                        : $"{{\"ok\":false,\"message\":\"{msg}\"}}\n";

                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
                    ctx.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                    ctx.Response.Close();
                    return;
                }

                // Legacy developer payload handling
                string type = "";
                string message = "";
                try
                {
                    var payload = JsonSerializer.Deserialize<DeveloperEventPayload>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (payload != null)
                    {
                        type = payload.Type ?? "";
                        message = payload.Message ?? "";
                    }
                }
                catch
                {
                    type = body.Trim();
                }

                _dispatcher.InvokeAsync(() => HandleEvent(type.ToLowerInvariant(), message));
            }
            else
            {
                // GET request (legacy query string support)
                string type = ctx.Request.QueryString["type"] ?? "";
                string message = ctx.Request.QueryString["message"] ?? ctx.Request.QueryString["msg"] ?? "";
                _dispatcher.InvokeAsync(() => HandleEvent(type.ToLowerInvariant(), message));
            }

            // Return 200 OK for legacy endpoint
            byte[] legacyResponse = Encoding.UTF8.GetBytes("{\"ok\":true}\n");
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.OutputStream.Write(legacyResponse, 0, legacyResponse.Length);
            ctx.Response.Close();
        }
        catch (Exception)
        {
            if (_running)
                DebugLog.Write("DeveloperEventService request processing error.");
        }
    }

    /// <summary>Trigger an event reaction on the pet.</summary>
    public void HandleEvent(string type, string? customMessage = null)
    {
        if (_engine == null) return;

        switch (type)
        {
            case "build_success":
            case "build_pass":
            case "build_ok":
                BuildTestEventReceived?.Invoke("Local Build", BuildTestResultStatus.Success, !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "Build passed successfully");
                _engine.Personality.OnPositiveEvent();
                if (_settings.EnableDevBuildReactions)
                {
                    _engine.TriggerDeveloperReaction(PetState.Celebrate,
                        !string.IsNullOrWhiteSpace(customMessage) ? customMessage : _engine.Personality.GetBuildSuccessLine());
                }
                break;

            case "build_failure":
            case "build_fail":
            case "build_error":
                BuildTestEventReceived?.Invoke("Local Build", BuildTestResultStatus.Failure, !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "Build failed");
                _engine.Personality.OnNegativeEvent();
                if (_settings.EnableDevBuildReactions)
                {
                    _engine.TriggerDeveloperReaction(PetState.Annoyed,
                        !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "Build broke?! Wasn't me! 💥");
                }
                break;

            case "test_pass":
            case "tests_pass":
            case "test_success":
                BuildTestEventReceived?.Invoke("Test Suite", BuildTestResultStatus.Success, !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "All tests passed");
                _engine.Personality.OnPositiveEvent();
                if (_settings.EnableDevTestReactions)
                {
                    _engine.TriggerDeveloperReaction(PetState.Proud,
                        !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "All tests green! Ship it! ✨");
                }
                break;

            case "test_fail":
            case "tests_fail":
            case "test_failure":
                BuildTestEventReceived?.Invoke("Test Suite", BuildTestResultStatus.Failure, !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "Tests failed");
                _engine.Personality.OnNegativeEvent();
                if (_settings.EnableDevTestReactions)
                {
                    _engine.TriggerDeveloperReaction(PetState.Surprised,
                        !string.IsNullOrWhiteSpace(customMessage) ? customMessage : _engine.Personality.GetTestFailureLine());
                }
                break;


            case "command_complete":
            case "command_done":
            case "done":
                _engine.Personality.OnPositiveEvent();
                _engine.TriggerDeveloperReaction(PetState.Jump,
                    !string.IsNullOrWhiteSpace(customMessage) ? customMessage : "Done! What's next? ⚡");
                break;

            default:
                if (!string.IsNullOrWhiteSpace(customMessage))
                {
                    _engine.ShowSpeech(customMessage, 3.5);
                }
                break;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
