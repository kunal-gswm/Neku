using System.Text.Json;
using Neku.Protocol;

namespace Neku.Cli;

public static class OutputFormatter
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void PrintJson(object? obj)
    {
        string json = JsonSerializer.Serialize(obj, IndentedJson);
        Console.WriteLine(json);
    }

    public static void PrintStatus(JsonElement data)
    {
        string companion = data.TryGetProperty("companionDisplayName", out var compName) ? compName.GetString() ?? "Neku" : "Neku";
        bool isHidden = data.TryGetProperty("isPetHidden", out var h) && h.GetBoolean();
        bool isPaused = data.TryGetProperty("isPetPaused", out var p) && p.GetBoolean();
        string petState = data.TryGetProperty("petState", out var ps) ? ps.GetString() ?? "Idle" : "Idle";

        string ver = data.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.3" : "1.0.3";
        Console.WriteLine($"Neku Desktop Companion (v{ver})");
        Console.WriteLine($"----------------------------------------");
        Console.WriteLine($"Companion   : {companion}");
        Console.WriteLine($"Pet State   : {petState} ({(isHidden ? "Hidden" : "Visible")}, {(isPaused ? "Paused" : "Active")})");

        if (data.TryGetProperty("focus", out var focus))
        {
            string fState = focus.TryGetProperty("state", out var fs) ? fs.GetString() ?? "Idle" : "Idle";
            string rem = focus.TryGetProperty("remaining", out var fr) ? fr.GetString() ?? "00:00" : "00:00";
            string? task = focus.TryGetProperty("task", out var ft) ? ft.GetString() : null;

            if (fState != "Idle")
            {
                Console.WriteLine($"Focus Timer : {fState} ({rem}){(task != null ? $" - Task: {task}" : "")}");
            }
            else
            {
                Console.WriteLine($"Focus Timer : Idle");
            }
        }

        if (data.TryGetProperty("tasks", out var tasks))
        {
            int pending = tasks.TryGetProperty("pending", out var tp) ? tp.GetInt32() : 0;
            int total = tasks.TryGetProperty("total", out var tt) ? tt.GetInt32() : 0;
            Console.WriteLine($"Tasks       : {pending} pending ({total} total)");
        }

        if (data.TryGetProperty("notes", out var notes))
        {
            int count = notes.TryGetProperty("total", out var nt) ? nt.GetInt32() : 0;
            Console.WriteLine($"Quick Notes : {count} notes");
        }
    }

    public static void PrintTaskList(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            Console.WriteLine("No tasks found.");
            return;
        }

        Console.WriteLine($"{"ID",-8} {"STATUS",-11} {"PRIORITY",-9} {"TITLE",-35} {"PROJECT"}");
        Console.WriteLine(new string('-', 75));

        foreach (var item in data.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
            string shortId = id.Length > 8 ? id[..8] : id;
            bool isCompleted = item.TryGetProperty("isCompleted", out var compElem) && compElem.GetBoolean();
            string status = isCompleted ? "[Completed]" : "[Pending]";
            string priority = item.TryGetProperty("priority", out var priElem) ? priElem.GetString() ?? "Normal" : "Normal";
            string title = item.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "" : "";
            string project = item.TryGetProperty("project", out var projElem) ? projElem.GetString() ?? "" : "";

            if (title.Length > 34) title = title[..31] + "...";

            Console.WriteLine($"{shortId,-8} {status,-11} {priority,-9} {title,-35} {project}");
        }
    }

    public static void PrintNoteList(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            Console.WriteLine("No notes found.");
            return;
        }

        Console.WriteLine($"{"ID",-8} {"UPDATED",-20} {"TITLE",-30} {"PREVIEW"}");
        Console.WriteLine(new string('-', 75));

        foreach (var item in data.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
            string shortId = id.Length > 8 ? id[..8] : id;
            string updated = item.TryGetProperty("updatedAt", out var upElem) ? upElem.GetString() ?? "" : "";
            string title = item.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "" : "";
            string preview = item.TryGetProperty("preview", out var prevElem) ? prevElem.GetString() ?? "" : "";

            if (title.Length > 29) title = title[..26] + "...";
            if (preview.Length > 30) preview = preview[..27] + "...";

            Console.WriteLine($"{shortId,-8} {updated,-20} {title,-30} {preview}");
        }
    }

    public static void PrintNoteDetail(JsonElement data)
    {
        string id = data.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
        string title = data.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Untitled" : "Untitled";
        string updated = data.TryGetProperty("updatedAt", out var upElem) ? upElem.GetString() ?? "" : "";
        string content = data.TryGetProperty("content", out var contentElem) ? contentElem.GetString() ?? "" : "";

        Console.WriteLine($"Title   : {title}");
        Console.WriteLine($"ID      : {id}");
        Console.WriteLine($"Updated : {updated}");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine(content);
    }

    public static void PrintCompanionList(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            Console.WriteLine("No companions discovered.");
            return;
        }

        Console.WriteLine($"{"ACT",-4} {"ID",-20} {"DISPLAY NAME",-30} {"TYPE"}");
        Console.WriteLine(new string('-', 65));

        foreach (var item in data.EnumerateArray())
        {
            bool isActive = item.TryGetProperty("isActive", out var actElem) && actElem.GetBoolean();
            string actMarker = isActive ? " *  " : "    ";
            string id = item.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
            string name = item.TryGetProperty("displayName", out var nameElem) ? nameElem.GetString() ?? id : id;
            bool isCustom = item.TryGetProperty("isCustom", out var customElem) && customElem.GetBoolean();
            string type = isCustom ? "Custom" : "Built-in";

            Console.WriteLine($"{actMarker}{id,-20} {name,-30} {type}");
        }
    }
}
