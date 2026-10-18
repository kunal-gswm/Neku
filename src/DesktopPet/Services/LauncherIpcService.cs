using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopPet.Services;

/// <summary>
/// Provides local per-user IPC via named pipes so that secondary launches (e.g. via the
/// Ctrl+Alt+N desktop/Start Menu shortcut) signal the primary running Neku instance
/// without spawning duplicate instances or showing modal dialogs.
/// </summary>
public sealed class LauncherIpcService : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _currentServer;
    private Task? _listenerTask;
    private bool _disposed;

    public event Action? ActivationRequested;

    public LauncherIpcService(string? pipeName = null)
    {
        _pipeName = pipeName ?? GetDefaultPipeName();
    }

    /// <summary>
    /// Generates a sanitized per-user pipe name scoped to the current Windows user session.
    /// </summary>
    public static string GetDefaultPipeName()
    {
        string user = Regex.Replace(Environment.UserName ?? "user", @"[^a-zA-Z0-9_\-]", "");
        if (string.IsNullOrWhiteSpace(user)) user = "default";
        return $"Neku.LauncherPipe.{user}";
    }

    /// <summary>Starts listening asynchronously for activation requests from secondary launches.</summary>
    public void Start()
    {
        if (_disposed || _listenerTask != null) return;
        _listenerTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _currentServer = server;
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                string? command = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);

                if (command is "ACTIVATE" or "LAUNCH")
                {
                    ActivationRequested?.Invoke();
                }

                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync("OK").ConfigureAwait(false);
                await writer.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                {
                    DebugLog.Write($"[LauncherIPC] Server loop error: {ex.Message}");
                    try { await Task.Delay(200, _cts.Token).ConfigureAwait(false); } catch { break; }
                }
            }
            finally
            {
                try { server?.Dispose(); } catch { }
                _currentServer = null;
            }
        }
    }

    /// <summary>
    /// Connects to a running instance via named pipe and signals it to activate.
    /// Returns true if successfully delivered, or false if timed out or failed.
    /// </summary>
    public static bool TrySignalRunningInstance(int timeoutMs = 800, string? pipeName = null)
    {
        try
        {
            string targetPipe = pipeName ?? GetDefaultPipeName();
            using var client = new NamedPipeClientStream(".", targetPipe, PipeDirection.InOut);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine("ACTIVATE");
            writer.Flush();

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            string? ack = reader.ReadLine();
            return ack == "OK";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LauncherIPC] TrySignalRunningInstance failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _currentServer?.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}
