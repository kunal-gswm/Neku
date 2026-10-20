using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Neku.Protocol;

public sealed class IpcClientException : Exception
{
    public bool IsNotRunning { get; }

    public IpcClientException(string message, bool isNotRunning = false) : base(message)
    {
        IsNotRunning = isNotRunning;
    }
}

/// <summary>
/// Lightweight named-pipe IPC client communicating with the running Neku desktop application.
/// </summary>
public static class IpcClient
{
    /// <summary>
    /// Sends a command to the running Neku desktop companion via named pipe.
    /// Throws <see cref="IpcClientException"/> if Neku is not running or if communication fails.
    /// </summary>
    public static async Task<NekuIpcResponse> SendCommandAsync(
        string command,
        Dictionary<string, string>? args = null,
        int timeoutMs = NekuIpcProtocol.DefaultTimeoutMs,
        string? customPipeName = null)
    {
        string pipeName = customPipeName ?? NekuIpcProtocol.GetPipeName();

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            var connectTask = client.ConnectAsync(timeoutMs);
            await connectTask.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new IpcClientException(
                "Neku desktop companion is not running.\nStart it with 'neku launch' or from the Windows Start menu.",
                isNotRunning: true);
        }
        catch (IOException ex)
        {
            throw new IpcClientException(
                $"Could not connect to Neku desktop companion: {ex.Message}\nStart it with 'neku launch'.",
                isNotRunning: true);
        }
        catch (Exception ex)
        {
            throw new IpcClientException(
                $"IPC connection error: {ex.Message}",
                isNotRunning: true);
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            var req = new NekuIpcRequest
            {
                Command = command,
                Args = args
            };

            string requestJson = JsonSerializer.Serialize(req, NekuIpcProtocol.JsonOptions);
            byte[] reqBytes = Encoding.UTF8.GetBytes(requestJson);
            if (reqBytes.Length > NekuIpcProtocol.MaxPayloadBytes)
            {
                throw new IpcClientException($"Request exceeds 64KB payload limit ({reqBytes.Length} bytes).");
            }

            await writer.WriteLineAsync(requestJson).ConfigureAwait(false);
            await writer.FlushAsync(cts.Token).ConfigureAwait(false);

            // Read response with bounded size
            var sb = new StringBuilder();
            int totalBytes = 0;

            while (true)
            {
                var readLineTask = reader.ReadLineAsync(cts.Token);
                string? line = await readLineTask.ConfigureAwait(false);
                if (line == null) break;

                totalBytes += Encoding.UTF8.GetByteCount(line);
                if (totalBytes > NekuIpcProtocol.MaxPayloadBytes)
                {
                    throw new IpcClientException("Response exceeded 64KB payload limit.");
                }

                sb.Append(line);
                break; // Single line JSON response
            }

            string responseJson = sb.ToString().Trim();
            if (string.IsNullOrEmpty(responseJson))
            {
                throw new IpcClientException("Received empty response from Neku desktop companion.");
            }

            var response = JsonSerializer.Deserialize<NekuIpcResponse>(responseJson, NekuIpcProtocol.JsonOptions);
            if (response == null)
            {
                throw new IpcClientException("Failed to parse response from Neku desktop companion.");
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            throw new IpcClientException($"Command '{command}' timed out after {timeoutMs}ms.");
        }
        catch (IpcClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IpcClientException($"Failed communicating with Neku desktop companion: {ex.Message}");
        }
    }
}
