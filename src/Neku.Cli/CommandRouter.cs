using System.Text.Json;
using Neku.Protocol;

namespace Neku.Cli;

public static class CommandRouter
{
    public static async Task<int> RouteAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "--help" or "-h" or "help":
                    PrintHelp();
                    return 0;

                case "--version" or "-v" or "version":
                    string cliVer = typeof(Program).Assembly.GetName().Version is { } ver ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.2";
                    if (jsonMode) OutputFormatter.PrintJson(new { version = cliVer, name = "Neku CLI" });
                    else Console.WriteLine($"Neku {cliVer}");
                    return 0;

                case "status":
                    return await HandleStatusAsync(jsonMode);

                case "doctor":
                    return await HandleDoctorAsync(jsonMode);

                case "launch":
                    return await HandleLaunchAsync(jsonMode);

                case "quit":
                    return await HandleQuitAsync(jsonMode);

                case "pet":
                    return await HandlePetAsync(args.Skip(1).ToArray(), jsonMode);

                case "focus":
                    return await HandleFocusAsync(args.Skip(1).ToArray(), jsonMode);

                case "task":
                    return await HandleTaskAsync(args.Skip(1).ToArray(), jsonMode);

                case "note":
                    return await HandleNoteAsync(args.Skip(1).ToArray(), jsonMode);

                case "companion":
                    return await HandleCompanionAsync(args.Skip(1).ToArray(), jsonMode);

                case "event":
                    return await HandleEventAsync(args.Skip(1).ToArray(), jsonMode);

                default:
                    if (jsonMode)
                    {
                        OutputFormatter.PrintJson(new { ok = false, error = $"Unknown command '{cmd}'. Use 'neku --help' for usage." });
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: Unknown command '{cmd}'.");
                        Console.Error.WriteLine("Run 'neku --help' for available commands.");
                    }
                    return 2;
            }
        }
        catch (IpcClientException ex)
        {
            if (jsonMode)
            {
                OutputFormatter.PrintJson(new { ok = false, error = ex.Message, isNotRunning = ex.IsNotRunning });
            }
            else
            {
                Console.Error.WriteLine(ex.Message);
            }
            return 1;
        }
        catch (Exception ex)
        {
            if (jsonMode)
            {
                OutputFormatter.PrintJson(new { ok = false, error = ex.Message });
            }
            else
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
            return 1;
        }
    }

    private static async Task<int> HandleStatusAsync(bool jsonMode)
    {
        var resp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Status);
        if (!resp.Ok)
        {
            if (jsonMode) OutputFormatter.PrintJson(resp);
            else Console.Error.WriteLine($"Error: {resp.Error ?? "Failed to retrieve status."}");
            return 1;
        }

        if (jsonMode)
        {
            OutputFormatter.PrintJson(resp);
        }
        else if (resp.Data is JsonElement je)
        {
            OutputFormatter.PrintStatus(je);
        }
        else
        {
            Console.WriteLine(resp.Message ?? "Neku is running.");
        }
        return 0;
    }

    private static async Task<int> HandleDoctorAsync(bool jsonMode)
    {
        var report = await DoctorCommand.RunDiagnosticsAsync();
        if (jsonMode)
        {
            OutputFormatter.PrintJson(report);
        }
        else
        {
            DoctorCommand.RenderHuman(report);
        }
        return 0;
    }

    private static async Task<int> HandleLaunchAsync(bool jsonMode)
    {
        var result = await Launcher.LaunchOrActivateAsync();
        if (jsonMode)
        {
            OutputFormatter.PrintJson(result);
        }
        else
        {
            if (result.Success)
            {
                Console.WriteLine(result.Message);
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Message}");
            }
        }
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleQuitAsync(bool jsonMode)
    {
        var resp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Quit);
        if (jsonMode)
        {
            OutputFormatter.PrintJson(resp);
        }
        else
        {
            Console.WriteLine(resp.Message ?? "Neku is shutting down.");
        }
        return resp.Ok ? 0 : 1;
    }

    private static async Task<int> HandlePetAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing pet subcommand. Use: show, hide, pause, resume, status." });
            else Console.Error.WriteLine("Usage: neku pet <show|hide|pause|resume|status>");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        string command = sub switch
        {
            "show" => NekuIpcProtocol.Commands.PetShow,
            "hide" => NekuIpcProtocol.Commands.PetHide,
            "pause" => NekuIpcProtocol.Commands.PetPause,
            "resume" => NekuIpcProtocol.Commands.PetResume,
            "status" => NekuIpcProtocol.Commands.PetStatus,
            _ => ""
        };

        if (string.IsNullOrEmpty(command))
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = $"Unknown pet subcommand '{sub}'." });
            else Console.Error.WriteLine($"Error: Unknown pet subcommand '{sub}'. Expected show, hide, pause, resume, or status.");
            return 2;
        }

        var resp = await IpcClient.SendCommandAsync(command);
        if (jsonMode)
        {
            OutputFormatter.PrintJson(resp);
        }
        else
        {
            if (resp.Ok)
            {
                if (sub == "status" && resp.Data is JsonElement je)
                {
                    string comp = je.TryGetProperty("companionDisplayName", out var c) ? c.GetString() ?? "" : "";
                    bool hidden = je.TryGetProperty("hidden", out var h) && h.GetBoolean();
                    bool paused = je.TryGetProperty("paused", out var p) && p.GetBoolean();
                    string state = je.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    int x = je.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
                    int y = je.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
                    Console.WriteLine($"Companion : {comp}");
                    Console.WriteLine($"Visibility: {(hidden ? "Hidden" : "Visible")}");
                    Console.WriteLine($"Activity  : {(paused ? "Paused" : "Active")} ({state})");
                    Console.WriteLine($"Position  : ({x}, {y})");
                }
                else
                {
                    Console.WriteLine(resp.Message);
                }
            }
            else
            {
                Console.Error.WriteLine($"Error: {resp.Error}");
            }
        }
        return resp.Ok ? 0 : 1;
    }

    private static async Task<int> HandleFocusAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing focus subcommand. Use: start, status, stop, pause, resume." });
            else Console.Error.WriteLine("Usage: neku focus <start|status|stop|pause|resume>");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "start":
                var focusArgs = new Dictionary<string, string>();
                for (int i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "--minutes" || args[i] == "-m") && i + 1 < args.Length)
                    {
                        focusArgs["minutes"] = args[++i];
                    }
                    else if ((args[i] == "--task" || args[i] == "-t") && i + 1 < args.Length)
                    {
                        focusArgs["task"] = args[++i];
                    }
                    else if ((args[i] == "--project" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        focusArgs["project"] = args[++i];
                    }
                    else if (int.TryParse(args[i], out int positionalMins))
                    {
                        focusArgs["minutes"] = positionalMins.ToString();
                    }
                }

                var startResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.FocusStart, focusArgs);
                if (jsonMode) OutputFormatter.PrintJson(startResp);
                else if (startResp.Ok) Console.WriteLine(startResp.Message);
                else Console.Error.WriteLine($"Error: {startResp.Error}");
                return startResp.Ok ? 0 : 1;

            case "status":
                var statusResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.FocusStatus);
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(statusResp);
                }
                else if (statusResp.Ok && statusResp.Data is JsonElement je)
                {
                    string state = je.TryGetProperty("state", out var s) ? s.GetString() ?? "Idle" : "Idle";
                    string rem = je.TryGetProperty("remaining", out var r) ? r.GetString() ?? "00:00" : "00:00";
                    string? task = je.TryGetProperty("task", out var t) ? t.GetString() : null;
                    string? proj = je.TryGetProperty("project", out var p) ? p.GetString() : null;

                    Console.WriteLine($"State     : {state}");
                    Console.WriteLine($"Remaining : {rem}");
                    if (!string.IsNullOrEmpty(task)) Console.WriteLine($"Task      : {task}");
                    if (!string.IsNullOrEmpty(proj)) Console.WriteLine($"Project   : {proj}");
                }
                else
                {
                    Console.WriteLine(statusResp.Message ?? "Focus timer idle.");
                }
                return statusResp.Ok ? 0 : 1;

            case "stop":
                var stopResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.FocusStop);
                if (jsonMode) OutputFormatter.PrintJson(stopResp);
                else Console.WriteLine(stopResp.Message);
                return stopResp.Ok ? 0 : 1;

            case "pause":
                var pauseResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.FocusPause);
                if (jsonMode) OutputFormatter.PrintJson(pauseResp);
                else Console.WriteLine(pauseResp.Message);
                return pauseResp.Ok ? 0 : 1;

            case "resume":
                var resumeResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.FocusResume);
                if (jsonMode) OutputFormatter.PrintJson(resumeResp);
                else Console.WriteLine(resumeResp.Message);
                return resumeResp.Ok ? 0 : 1;

            default:
                if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = $"Unknown focus subcommand '{sub}'." });
                else Console.Error.WriteLine($"Error: Unknown focus subcommand '{sub}'. Expected start, status, stop, pause, or resume.");
                return 2;
        }
    }

    private static async Task<int> HandleTaskAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing task subcommand. Use: add, list, complete, delete." });
            else Console.Error.WriteLine("Usage: neku task <add|list|complete|delete>");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "add":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing task title." });
                    else Console.Error.WriteLine("Usage: neku task add <title> [--priority <low|normal|high|urgent>] [--project <name>]");
                    return 2;
                }

                var taskArgs = new Dictionary<string, string>();
                string title = "";
                for (int i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "--priority" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        taskArgs["priority"] = args[++i];
                    }
                    else if ((args[i] == "--project" || args[i] == "-proj") && i + 1 < args.Length)
                    {
                        taskArgs["project"] = args[++i];
                    }
                    else if ((args[i] == "--description" || args[i] == "-d") && i + 1 < args.Length)
                    {
                        taskArgs["description"] = args[++i];
                    }
                    else
                    {
                        title = string.IsNullOrEmpty(title) ? args[i] : $"{title} {args[i]}";
                    }
                }

                taskArgs["title"] = title;
                var addResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskAdd, taskArgs);
                if (jsonMode) OutputFormatter.PrintJson(addResp);
                else if (addResp.Ok) Console.WriteLine(addResp.Message);
                else Console.Error.WriteLine($"Error: {addResp.Error}");
                return addResp.Ok ? 0 : 1;

            case "list":
                bool showAll = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase) || a.Equals("-a", StringComparison.OrdinalIgnoreCase));
                var listArgs = new Dictionary<string, string> { ["all"] = showAll ? "true" : "false" };
                var listResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskList, listArgs);
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(listResp);
                }
                else if (listResp.Ok && listResp.Data is JsonElement je)
                {
                    OutputFormatter.PrintTaskList(je);
                }
                else
                {
                    Console.WriteLine(listResp.Message ?? "No tasks found.");
                }
                return listResp.Ok ? 0 : 1;

            case "complete":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing task ID." });
                    else Console.Error.WriteLine("Usage: neku task complete <task-id>");
                    return 2;
                }
                string compId = args[1];
                var compResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskComplete, new() { ["id"] = compId });
                if (jsonMode) OutputFormatter.PrintJson(compResp);
                else if (compResp.Ok) Console.WriteLine(compResp.Message);
                else Console.Error.WriteLine($"Error: {compResp.Error}");
                return compResp.Ok ? 0 : 1;

            case "delete":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing task ID." });
                    else Console.Error.WriteLine("Usage: neku task delete <task-id>");
                    return 2;
                }
                string delId = args[1];
                var delResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.TaskDelete, new() { ["id"] = delId });
                if (jsonMode) OutputFormatter.PrintJson(delResp);
                else if (delResp.Ok) Console.WriteLine(delResp.Message);
                else Console.Error.WriteLine($"Error: {delResp.Error}");
                return delResp.Ok ? 0 : 1;

            default:
                if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = $"Unknown task subcommand '{sub}'." });
                else Console.Error.WriteLine($"Error: Unknown task subcommand '{sub}'. Expected add, list, complete, or delete.");
                return 2;
        }
    }

    private static async Task<int> HandleNoteAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing note subcommand. Use: add, list, search, view, delete." });
            else Console.Error.WriteLine("Usage: neku note <add|list|search|view|delete>");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "add":
                var noteArgs = new Dictionary<string, string>();
                string? explicitTitle = null;
                string? explicitBody = null;
                var positional = new List<string>();

                for (int i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "--title" || args[i] == "-t") && i + 1 < args.Length)
                    {
                        explicitTitle = args[++i];
                    }
                    else if ((args[i] == "--body" || args[i] == "-b" || args[i] == "--content") && i + 1 < args.Length)
                    {
                        explicitBody = args[++i];
                    }
                    else
                    {
                        positional.Add(args[i]);
                    }
                }

                if (!string.IsNullOrEmpty(explicitTitle))
                {
                    noteArgs["title"] = explicitTitle;
                    noteArgs["body"] = explicitBody ?? (positional.Count > 0 ? string.Join(" ", positional) : "");
                }
                else if (positional.Count > 0)
                {
                    noteArgs["title"] = positional[0];
                    noteArgs["body"] = explicitBody ?? (positional.Count > 1 ? string.Join(" ", positional.Skip(1)) : "");
                }
                else
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Note requires a title or content." });
                    else Console.Error.WriteLine("Usage: neku note add --title <title> --body <body>");
                    return 2;
                }

                var addResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteAdd, noteArgs);
                if (jsonMode) OutputFormatter.PrintJson(addResp);
                else if (addResp.Ok) Console.WriteLine(addResp.Message);
                else Console.Error.WriteLine($"Error: {addResp.Error}");
                return addResp.Ok ? 0 : 1;

            case "list":
                var listResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteList);
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(listResp);
                }
                else if (listResp.Ok && listResp.Data is JsonElement je)
                {
                    OutputFormatter.PrintNoteList(je);
                }
                else
                {
                    Console.WriteLine(listResp.Message ?? "No notes found.");
                }
                return listResp.Ok ? 0 : 1;

            case "search":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing search query." });
                    else Console.Error.WriteLine("Usage: neku note search <query>");
                    return 2;
                }
                string query = string.Join(" ", args.Skip(1));
                var searchResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteSearch, new() { ["query"] = query });
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(searchResp);
                }
                else if (searchResp.Ok && searchResp.Data is JsonElement je)
                {
                    OutputFormatter.PrintNoteList(je);
                }
                else
                {
                    Console.WriteLine(searchResp.Message ?? $"No notes matching '{query}'.");
                }
                return searchResp.Ok ? 0 : 1;

            case "view" or "show" or "get":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing note ID." });
                    else Console.Error.WriteLine("Usage: neku note view <note-id>");
                    return 2;
                }
                string viewId = args[1];
                var getResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteGet, new() { ["id"] = viewId });
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(getResp);
                }
                else if (getResp.Ok && getResp.Data is JsonElement je)
                {
                    OutputFormatter.PrintNoteDetail(je);
                }
                else
                {
                    Console.Error.WriteLine($"Error: {getResp.Error}");
                }
                return getResp.Ok ? 0 : 1;

            case "delete":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing note ID." });
                    else Console.Error.WriteLine("Usage: neku note delete <note-id>");
                    return 2;
                }
                string delId = args[1];
                var delResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.NoteDelete, new() { ["id"] = delId });
                if (jsonMode) OutputFormatter.PrintJson(delResp);
                else if (delResp.Ok) Console.WriteLine(delResp.Message);
                else Console.Error.WriteLine($"Error: {delResp.Error}");
                return delResp.Ok ? 0 : 1;

            default:
                if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = $"Unknown note subcommand '{sub}'." });
                else Console.Error.WriteLine($"Error: Unknown note subcommand '{sub}'. Expected add, list, search, view, or delete.");
                return 2;
        }
    }

    private static async Task<int> HandleCompanionAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing companion subcommand. Use: list, set." });
            else Console.Error.WriteLine("Usage: neku companion <list|set <name>>");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "list":
                var listResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.CompanionList);
                if (jsonMode)
                {
                    OutputFormatter.PrintJson(listResp);
                }
                else if (listResp.Ok && listResp.Data is JsonElement je)
                {
                    OutputFormatter.PrintCompanionList(je);
                }
                else
                {
                    Console.WriteLine(listResp.Message ?? "No companions found.");
                }
                return listResp.Ok ? 0 : 1;

            case "set":
                if (args.Length < 2)
                {
                    if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing companion name." });
                    else Console.Error.WriteLine("Usage: neku companion set <name>");
                    return 2;
                }
                string name = args[1];
                var setResp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.CompanionSet, new() { ["name"] = name });
                if (jsonMode) OutputFormatter.PrintJson(setResp);
                else if (setResp.Ok) Console.WriteLine(setResp.Message);
                else Console.Error.WriteLine($"Error: {setResp.Error}");
                return setResp.Ok ? 0 : 1;

            default:
                if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = $"Unknown companion subcommand '{sub}'." });
                else Console.Error.WriteLine($"Error: Unknown companion subcommand '{sub}'. Expected list or set.");
                return 2;
        }
    }

    private static async Task<int> HandleEventAsync(string[] args, bool jsonMode)
    {
        if (args.Length == 0)
        {
            if (jsonMode) OutputFormatter.PrintJson(new { ok = false, error = "Missing event category (build, test)." });
            else Console.Error.WriteLine("Usage: neku event <build|test> <success|failure|pass> [--message <msg>]");
            return 2;
        }

        string cat = args[0].ToLowerInvariant();
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        string? message = null;

        for (int i = 2; i < args.Length; i++)
        {
            if ((args[i] == "--message" || args[i] == "-m") && i + 1 < args.Length)
            {
                message = args[++i];
            }
        }

        string eventType;
        if (cat == "build" && (action == "success" || action == "pass" || action == "ok"))
            eventType = "build_success";
        else if (cat == "build" && (action == "failure" || action == "fail" || action == "error"))
            eventType = "build_failure";
        else if (cat == "test" && (action == "pass" || action == "success" || action == "ok"))
            eventType = "test_pass";
        else if (cat == "test" && (action == "fail" || action == "failure" || action == "error"))
            eventType = "test_failure";
        else if (args.Length == 1)
            eventType = cat;
        else
            eventType = $"{cat}_{action}";

        var resp = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Event, new()
        {
            ["type"] = eventType,
            ["message"] = message ?? ""
        });

        if (jsonMode) OutputFormatter.PrintJson(resp);
        else if (resp.Ok) Console.WriteLine(resp.Message);
        else Console.Error.WriteLine($"Error: {resp.Error}");
        return resp.Ok ? 0 : 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"Neku CLI - Command-Line Interface for the Neku Desktop Companion

Usage:
  neku [command] [options]

General Commands:
  status                     Show running status, companion, timer, and task counts
  doctor                     Run system and environment diagnostics
  launch                     Launch or bring the Neku desktop companion to front
  quit                       Gracefully exit the running desktop companion
  --help, -h                 Display this help information
  --version, -v              Display the application version

Pet Commands:
  pet show                   Unhide the desktop companion
  pet hide                   Hide the desktop companion
  pet pause                  Pause movement and animations
  pet resume                 Resume movement and animations
  pet status                 Show companion position, visibility, and animation state

Focus Commands:
  focus start [--minutes M]  Start a Pomodoro focus session (default: 25 mins)
  focus status               Display active focus timer and remaining time
  focus stop                 Stop and cancel the active focus session
  focus pause                Pause active focus countdown
  focus resume               Resume paused focus countdown

Task Commands:
  task add <title> [opts]    Create a new task (--priority <low|normal|high|urgent>, --project <name>)
  task list [--all]          List pending tasks (use --all to include completed)
  task complete <id>         Mark a task as completed
  task delete <id>           Delete a task

Note Commands:
  note add --title T --body B Create a quick note
  note list                  List recent notes
  note search <query>        Search notes by keyword
  note view <id>             Display full note title and contents
  note delete <id>           Delete a note

Companion Commands:
  companion list             List available built-in and custom companion packs
  companion set <name>       Live-switch the active desktop companion (e.g. Neku, cat, billu)

Developer Events:
  event build success [-m M] Trigger celebratory reaction for a passed build
  event build failure [-m M] Trigger annoyed reaction for a broken build
  event test pass [-m M]     Trigger proud reaction for passed unit tests
  event test failure [-m M]  Trigger reaction for test failures

Global Flags:
  --json                     Format command output as machine-readable JSON
  --help, -h                 Show help
  --version, -v              Show version");
    }
}
