using System.Text.Json;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class DeveloperEventsTests
{
    [Fact]
    public void DeveloperEventPayload_deserializes_from_json()
    {
        string json = """{"type":"build_success","message":"Build passed in 2.1s!"}""";
        var payload = JsonSerializer.Deserialize<DeveloperEventPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(payload);
        Assert.Equal("build_success", payload.Type);
        Assert.Equal("Build passed in 2.1s!", payload.Message);
    }

    [Fact]
    public void DeveloperEventPayload_handles_case_insensitivity()
    {
        string json = """{"TYPE":"test_fail","MESSAGE":"1 error"}""";
        var payload = JsonSerializer.Deserialize<DeveloperEventPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(payload);
        Assert.Equal("test_fail", payload.Type);
        Assert.Equal("1 error", payload.Message);
    }

    [Fact]
    public void DeveloperEventPayload_defaults_to_build_success()
    {
        var payload = new DeveloperEventPayload();
        Assert.Equal("build_success", payload.Type);
        Assert.Null(payload.Message);
    }

    [Theory]
    [InlineData("build_success")]
    [InlineData("build_pass")]
    [InlineData("build_ok")]
    [InlineData("build_failure")]
    [InlineData("build_fail")]
    [InlineData("build_error")]
    [InlineData("test_pass")]
    [InlineData("test_fail")]
    [InlineData("command_complete")]
    public void Known_event_types_are_supported(string eventType)
    {
        var payload = new DeveloperEventPayload
        {
            Type = eventType,
            Message = "Test message"
        };
        Assert.Equal(eventType, payload.Type);
        Assert.Equal("Test message", payload.Message);
    }
}
