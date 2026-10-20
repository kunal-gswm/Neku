using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Neku.Protocol;
using Xunit;

namespace DesktopPet.Tests;

public sealed class NekuIpcTests
{
    [Fact]
    public void PipeName_DefaultAndCustom_IsSanitizedAndFormatted()
    {
        string pipe = NekuIpcProtocol.GetPipeName();
        Assert.StartsWith("Neku.CommandPipe.", pipe);
        Assert.DoesNotContain(" ", pipe);

        string customPipe = NekuIpcProtocol.GetPipeName("Test User 123!@#");
        Assert.Equal("Neku.CommandPipe.TestUser123", customPipe);

        string emptyPipe = NekuIpcProtocol.GetPipeName("   ");
        Assert.Equal("Neku.CommandPipe.default", emptyPipe);
    }

    [Fact]
    public void RequestAndResponse_SerializationRoundTrip_Succeeds()
    {
        var req = new NekuIpcRequest
        {
            Command = "task.add",
            Args = new Dictionary<string, string>
            {
                ["title"] = "Build CLI tests",
                ["priority"] = "High"
            }
        };

        string json = JsonSerializer.Serialize(req, NekuIpcProtocol.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<NekuIpcRequest>(json, NekuIpcProtocol.JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("task.add", deserialized.Command);
        Assert.Equal("Build CLI tests", deserialized.GetArg("title"));
        Assert.Equal("High", deserialized.GetArg("priority"));
        Assert.Null(deserialized.GetArg("nonexistent"));

        var resp = NekuIpcResponse.Success("All good", new { count = 42 });
        string respJson = JsonSerializer.Serialize(resp, NekuIpcProtocol.JsonOptions);
        var desResp = JsonSerializer.Deserialize<NekuIpcResponse>(respJson, NekuIpcProtocol.JsonOptions);

        Assert.NotNull(desResp);
        Assert.True(desResp.Ok);
        Assert.Equal("All good", desResp.Message);
        Assert.Equal(200, desResp.Code);
    }

    [Fact]
    public async Task NekuIpcServer_BasicRequest_ReturnsExpectedResponse()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";

        using var server = new NekuIpcServer(req =>
        {
            if (req.Command == "ping")
            {
                return Task.FromResult(NekuIpcResponse.Success("pong", new { test = true }));
            }
            return Task.FromResult(NekuIpcResponse.Fail("unknown command"));
        }, pipeName);

        server.Start();
        await server.WaitForReadyAsync();

        // Connect client
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);

        using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        var requestObj = new NekuIpcRequest { Command = "ping" };
        await writer.WriteLineAsync(JsonSerializer.Serialize(requestObj, NekuIpcProtocol.JsonOptions));
        await writer.FlushAsync();

        string? responseJson = await reader.ReadLineAsync();
        Assert.NotNull(responseJson);

        var resp = JsonSerializer.Deserialize<NekuIpcResponse>(responseJson, NekuIpcProtocol.JsonOptions);
        Assert.NotNull(resp);
        Assert.True(resp.Ok);
        Assert.Equal("pong", resp.Message);
    }

    [Fact]
    public async Task NekuIpcServer_ExceedingPayloadLimit_ReturnsPayloadTooLargeError()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";

        using var server = new NekuIpcServer(req =>
        {
            return Task.FromResult(NekuIpcResponse.Success("ok"));
        }, pipeName);

        server.Start();
        await server.WaitForReadyAsync();

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);

        using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        // Send a massive payload (> 64 KB)
        string hugeString = new string('A', 70 * 1024);
        var hugeReq = new NekuIpcRequest
        {
            Command = "test",
            Args = new Dictionary<string, string> { ["huge"] = hugeString }
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(hugeReq, NekuIpcProtocol.JsonOptions));
        await writer.FlushAsync();

        string? responseJson = await reader.ReadLineAsync();
        Assert.NotNull(responseJson);

        var resp = JsonSerializer.Deserialize<NekuIpcResponse>(responseJson, NekuIpcProtocol.JsonOptions);
        Assert.NotNull(resp);
        Assert.False(resp.Ok);
        Assert.Equal(413, resp.Code);
        Assert.Contains("64KB", resp.Error);
    }

    [Fact]
    public async Task NekuIpcServer_EmptyOrMalformedRequest_ReturnsBadRequest()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";

        using var server = new NekuIpcServer(req => Task.FromResult(NekuIpcResponse.Success("ok")), pipeName);
        server.Start();
        await server.WaitForReadyAsync();

        // 1. Send malformed non-JSON string
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client.ConnectAsync(5000);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync("THIS IS NOT JSON {{{");
            await writer.FlushAsync();

            string? responseJson = await reader.ReadLineAsync();
            Assert.NotNull(responseJson);
            var resp = JsonSerializer.Deserialize<NekuIpcResponse>(responseJson, NekuIpcProtocol.JsonOptions);
            Assert.NotNull(resp);
            Assert.False(resp.Ok);
            Assert.Equal(400, resp.Code);
        }

        // Deterministically wait for the server to be ready for the next sequential client connection
        await server.WaitForNextInstanceReadyAsync();

        // 2. Send empty command
        using (var client2 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await client2.ConnectAsync(5000);
            using var writer2 = new StreamWriter(client2, Encoding.UTF8, leaveOpen: true);
            using var reader2 = new StreamReader(client2, Encoding.UTF8, leaveOpen: true);

            await writer2.WriteLineAsync("{\"command\":\"\"}");
            await writer2.FlushAsync();

            string? responseJson2 = await reader2.ReadLineAsync();
            Assert.NotNull(responseJson2);
            var resp2 = JsonSerializer.Deserialize<NekuIpcResponse>(responseJson2, NekuIpcProtocol.JsonOptions);
            Assert.NotNull(resp2);
            Assert.False(resp2.Ok);
            Assert.Equal(400, resp2.Code);
        }
    }
}
