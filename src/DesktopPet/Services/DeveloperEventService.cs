using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using DesktopPet.Engine;

namespace DesktopPet.Services;

public sealed class DeveloperEventPayload
{
    public string Type { get; set; } = "build_success";
    public string? Message { get; set; }
}

/// <summary>
/// Lightweight local endpoint for developer events (builds, tests, commands).
/// Completely disabled by default — zero threads, zero network sockets when off.
/// </summary>
public sealed class DeveloperEventService : IDisposable
{
    private HttpListener? _listener;
    private readonly PetEngine _engine;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private bool _running;

    public DeveloperEventService(PetEngine engine, AppSettings settings, Dispatcher dispatcher)
    {
        _engine = engine;
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (!_settings.EnableDeveloperEvents || _running) return;

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

            string type = "";
            string message = "";

            if (ctx.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                string body = reader.ReadToEnd();
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
            }
            else
            {
                type = ctx.Request.QueryString["type"] ?? "";
                message = ctx.Request.QueryString["message"] ?? ctx.Request.QueryString["msg"] ?? "";
            }

            // Dispatch reaction to UI thread
            _dispatcher.InvokeAsync(() => HandleEvent(type.ToLowerInvariant(), message));

            // Return 200 OK
            byte[] responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}\n");
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = 200;
            ctx.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            if (_running)
                DebugLog.Write($"DeveloperEventService request error: {ex.Message}");
        }
    }

    /// <summary>Trigger an event reaction on the pet.</summary>
    public void HandleEvent(string type, string? customMessage = null)
    {
        switch (type)
        {
            case "build_success":
            case "build_pass":
            case "build_ok":
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
