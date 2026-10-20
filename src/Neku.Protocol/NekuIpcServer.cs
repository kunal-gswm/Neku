using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Neku.Protocol;

/// <summary>
/// Named-pipe IPC server running within the DesktopPet host to handle commands from the Neku CLI.
/// Enforces per-user pipe scoping, local access, payload size bounds (64 KB), and timeouts.
/// </summary>
public sealed class NekuIpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<NekuIpcRequest, Task<NekuIpcResponse>> _handler;
    private readonly Action<string>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile TaskCompletionSource _nextInstanceReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _listenerTask;
    private bool _disposed;

    /// <summary>
    /// Gets a task that completes once the server's named pipe has been created and is ready for client connections.
    /// </summary>
    public Task ReadyTask => _readyTcs.Task;

    /// <summary>
    /// Asynchronously waits until the named pipe server has successfully initialized and is ready to accept connections.
    /// </summary>
    public async Task WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            await _readyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _readyTcs.Task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously waits until the named pipe server has created its next instance and is ready to accept a subsequent connection.
    /// </summary>
    public async Task WaitForNextInstanceReadyAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            await _nextInstanceReadyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _nextInstanceReadyTcs.Task.ConfigureAwait(false);
        }
    }

    public NekuIpcServer(
        Func<NekuIpcRequest, Task<NekuIpcResponse>> handler,
        string? customPipeName = null,
        Action<string>? logger = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _pipeName = customPipeName ?? NekuIpcProtocol.GetPipeName();
        _logger = logger;
    }

    public void Start()
    {
        if (_disposed || _listenerTask != null) return;
        _listenerTask = Task.Run(ListenLoopAsync);
        _logger?.Invoke($"[NekuIpcServer] Listening on pipe: {_pipeName}");
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
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                // Signal initial server readiness and next instance readiness
                _readyTcs.TrySetResult();
                _nextInstanceReadyTcs.TrySetResult();

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                // Reset next instance TCS so callers can await the subsequent server instance
                _nextInstanceReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // Hand off client processing asynchronously so the listen loop immediately
                // creates and begins waiting on the next server instance.
                var activeStream = server;
                server = null; // Ownership transferred to ProcessAndDisposeConnectionAsync
                _ = Task.Run(() => ProcessAndDisposeConnectionAsync(activeStream, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                {
                    _readyTcs.TrySetException(ex);
                    _nextInstanceReadyTcs.TrySetException(ex);
                    _logger?.Invoke($"[NekuIpcServer] Error in listen loop: {ex.Message}");
                    try { await Task.Delay(100, _cts.Token).ConfigureAwait(false); } catch { break; }
                }
            }
            finally
            {
                try { server?.Dispose(); } catch { }
            }
        }
    }

    private async Task ProcessAndDisposeConnectionAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            await ProcessClientConnectionAsync(stream, ct).ConfigureAwait(false);
        }
        finally
        {
            try { stream.Dispose(); } catch { }
        }
    }

    private async Task ProcessClientConnectionAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);

            // Read line with a character/byte limit to prevent memory exhaustion
            var sb = new StringBuilder();
            int totalBytes = 0;
            bool exceeded = false;

            while (true)
            {
                var readTask = reader.ReadLineAsync(ct);
                // Apply a read timeout of 3000 ms
                var completedTask = await Task.WhenAny(readTask.AsTask(), Task.Delay(3000, ct)).ConfigureAwait(false);
                if (completedTask != readTask.AsTask())
                {
                    // Timeout
                    return;
                }

                string? line = await readTask.ConfigureAwait(false);
                if (line == null) break;

                totalBytes += Encoding.UTF8.GetByteCount(line);
                if (totalBytes > NekuIpcProtocol.MaxPayloadBytes)
                {
                    exceeded = true;
                    break;
                }

                sb.Append(line);
                break; // Single-line JSON request
            }

            if (exceeded)
            {
                var tooLargeResp = NekuIpcResponse.Fail("Request payload exceeds 64KB limit.", 413);
                string errJson = JsonSerializer.Serialize(tooLargeResp, NekuIpcProtocol.JsonOptions);
                await writer.WriteLineAsync(errJson).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            string requestJson = sb.ToString().Trim();
            if (string.IsNullOrEmpty(requestJson))
            {
                var emptyResp = NekuIpcResponse.Fail("Empty request received.", 400);
                string errJson = JsonSerializer.Serialize(emptyResp, NekuIpcProtocol.JsonOptions);
                await writer.WriteLineAsync(errJson).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            NekuIpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<NekuIpcRequest>(requestJson, NekuIpcProtocol.JsonOptions);
            }
            catch (Exception ex)
            {
                var parseErrResp = NekuIpcResponse.Fail($"Invalid JSON request: {ex.Message}", 400);
                string errJson = JsonSerializer.Serialize(parseErrResp, NekuIpcProtocol.JsonOptions);
                await writer.WriteLineAsync(errJson).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                var noCmdResp = NekuIpcResponse.Fail("Missing command name in request.", 400);
                string errJson = JsonSerializer.Serialize(noCmdResp, NekuIpcProtocol.JsonOptions);
                await writer.WriteLineAsync(errJson).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            // Execute handler
            NekuIpcResponse response;
            try
            {
                response = await _handler(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response = NekuIpcResponse.Fail($"Internal server error: {ex.Message}", 500);
            }

            string responseJson = JsonSerializer.Serialize(response, NekuIpcProtocol.JsonOptions);
            await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            _logger?.Invoke($"[NekuIpcServer] Client handling exception: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _readyTcs.TrySetCanceled();
        _nextInstanceReadyTcs.TrySetCanceled();
        _cts.Dispose();
    }
}
