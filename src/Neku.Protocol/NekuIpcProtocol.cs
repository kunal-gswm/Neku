using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Neku.Protocol;

/// <summary>
/// Defines the named-pipe IPC protocol models, constants, and limits for communication
/// between the Neku desktop companion (server) and the Neku CLI (client).
/// </summary>
public static class NekuIpcProtocol
{
    public const int MaxPayloadBytes = 64 * 1024; // 64 KB bounded payload limit
    public const int DefaultTimeoutMs = 2500;     // 2.5 seconds timeout

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Generates the per-user command pipe name scoped to the current Windows user session.
    /// </summary>
    public static string GetPipeName(string? customUser = null)
    {
        string user = customUser ?? Environment.UserName ?? "user";
        user = Regex.Replace(user, @"[^a-zA-Z0-9_\-]", "");
        if (string.IsNullOrWhiteSpace(user)) user = "default";
        return $"Neku.CommandPipe.{user}";
    }

    // Command names
    public static class Commands
    {
        public const string Ping = "ping";
        public const string Status = "status";
        public const string Quit = "quit";

        public const string PetShow = "pet.show";
        public const string PetHide = "pet.hide";
        public const string PetPause = "pet.pause";
        public const string PetResume = "pet.resume";
        public const string PetStatus = "pet.status";

        public const string FocusStart = "focus.start";
        public const string FocusStatus = "focus.status";
        public const string FocusStop = "focus.stop";
        public const string FocusPause = "focus.pause";
        public const string FocusResume = "focus.resume";

        public const string TaskAdd = "task.add";
        public const string TaskList = "task.list";
        public const string TaskComplete = "task.complete";
        public const string TaskDelete = "task.delete";

        public const string NoteAdd = "note.add";
        public const string NoteList = "note.list";
        public const string NoteSearch = "note.search";
        public const string NoteGet = "note.get";
        public const string NoteDelete = "note.delete";

        public const string CompanionList = "companion.list";
        public const string CompanionSet = "companion.set";

        public const string Event = "event";
    }
}

/// <summary>
/// IPC request message sent from CLI to DesktopPet host.
/// </summary>
public sealed class NekuIpcRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public Dictionary<string, string>? Args { get; set; }

    public string? GetArg(string key)
    {
        if (Args != null && Args.TryGetValue(key, out var val))
            return val;
        return null;
    }
}

/// <summary>
/// IPC response message returned from DesktopPet host to CLI.
/// </summary>
public sealed class NekuIpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; } = 200;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    public static NekuIpcResponse Success(string message, object? data = null) =>
        new() { Ok = true, Message = message, Data = data, Code = 200 };

    public static NekuIpcResponse Fail(string error, int code = 400) =>
        new() { Ok = false, Error = error, Code = code };
}
