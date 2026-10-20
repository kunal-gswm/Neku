using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Neku.Protocol;
using Microsoft.Win32;

namespace Neku.Cli;

public sealed class DoctorReport
{
    public string CliVersion { get; set; } = "1.0.0";
    public string? CliPath { get; set; }
    public bool DesktopExecutableFound { get; set; }
    public string? DesktopExecutablePath { get; set; }
    public bool DesktopAppRunning { get; set; }
    public string IpcPipeName { get; set; } = "";
    public bool IpcConnected { get; set; }
    public string? ActiveCompanion { get; set; }
    public string AppDataDirectory { get; set; } = "";
    public bool AppDataExists { get; set; }
    public bool TasksFileExists { get; set; }
    public int TasksCount { get; set; }
    public bool NotesFileExists { get; set; }
    public int NotesCount { get; set; }
    public bool SettingsFileExists { get; set; }
    public bool UserPathConfigured { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

public static class DoctorCommand
{
    public static async Task<DoctorReport> RunDiagnosticsAsync()
    {
        var report = new DoctorReport();

        // 1. CLI Executable
        try
        {
            report.CliPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        }
        catch { }

        // 2. Desktop Executable
        string? exe = Launcher.ResolveNekuExecutable();
        report.DesktopExecutableFound = exe != null;
        report.DesktopExecutablePath = exe;

        // 3. IPC Connection & Running Status
        report.IpcPipeName = NekuIpcProtocol.GetPipeName();
        try
        {
            var ping = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Ping, timeoutMs: 500);
            report.IpcConnected = ping.Ok;
            report.DesktopAppRunning = ping.Ok;
            if (ping.Data is JsonElement je && je.TryGetProperty("activePet", out var petElem))
            {
                report.ActiveCompanion = petElem.GetString();
            }
        }
        catch
        {
            report.IpcConnected = false;
            report.DesktopAppRunning = false;
        }

        // 4. AppData Directory
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Neku");
        report.AppDataDirectory = appData;
        report.AppDataExists = Directory.Exists(appData);

        // 5. Files: tasks.json
        string tasksPath = Path.Combine(appData, "tasks.json");
        report.TasksFileExists = File.Exists(tasksPath);
        if (report.TasksFileExists)
        {
            try
            {
                string json = File.ReadAllText(tasksPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    report.TasksCount = doc.RootElement.GetArrayLength();
                }
            }
            catch { }
        }

        // 6. Files: notes.json
        string notesPath = Path.Combine(appData, "notes.json");
        report.NotesFileExists = File.Exists(notesPath);
        if (report.NotesFileExists)
        {
            try
            {
                string json = File.ReadAllText(notesPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    report.NotesCount = doc.RootElement.GetArrayLength();
                }
            }
            catch { }
        }

        // 7. Files: settings.json
        string settingsPath = Path.Combine(appData, "settings.json");
        report.SettingsFileExists = File.Exists(settingsPath);

        // 8. User PATH verification
        report.UserPathConfigured = CheckUserPath(report.CliPath);

        // Build human notes
        if (!report.DesktopExecutableFound)
        {
            report.Diagnostics.Add("Desktop companion executable (Neku.exe) was not found in standard paths.");
        }
        if (!report.DesktopAppRunning)
        {
            report.Diagnostics.Add("Neku desktop companion is not currently running. Use 'neku launch' to start it.");
        }
        if (!report.UserPathConfigured)
        {
            report.Diagnostics.Add("Neku CLI directory is not in User PATH. Consider running the installer or adding its bin folder to PATH.");
        }

        return report;
    }

    private static bool CheckUserPath(string? cliPath)
    {
        try
        {
            string? cliDir = !string.IsNullOrEmpty(cliPath) ? Path.GetDirectoryName(cliPath) : null;
            string standardBin = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Neku", "bin");

            // Check HKCU\Environment\Path
            using var envKey = Registry.CurrentUser.OpenSubKey("Environment");
            string? userPath = envKey?.GetValue("Path") as string;
            if (!string.IsNullOrWhiteSpace(userPath))
            {
                var parts = userPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Any(p => p.Equals(standardBin, StringComparison.OrdinalIgnoreCase) ||
                                  (cliDir != null && p.Equals(cliDir, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }

            // Check Process PATH
            string? procPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(procPath))
            {
                var parts = procPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Any(p => p.Equals(standardBin, StringComparison.OrdinalIgnoreCase) ||
                                  (cliDir != null && p.Equals(cliDir, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public static void RenderHuman(DoctorReport r)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("          Neku CLI Doctor               ");
        Console.WriteLine("========================================");
        Console.WriteLine();

        PrintCheck("CLI Version", $"{r.CliVersion} ({r.CliPath ?? "unknown"})", true);
        PrintCheck("Desktop Binary", r.DesktopExecutableFound ? r.DesktopExecutablePath! : "Not found in candidate paths", r.DesktopExecutableFound);
        PrintCheck("Neku Running", r.DesktopAppRunning ? $"Yes (Active Companion: {r.ActiveCompanion ?? "Neku"})" : "No (Start with 'neku launch')", r.DesktopAppRunning);
        PrintCheck("Local IPC Pipe", $"{r.IpcPipeName} ({(r.IpcConnected ? "Active" : "Offline")})", r.IpcConnected);
        PrintCheck("AppData Storage", $"{r.AppDataDirectory} ({(r.AppDataExists ? "OK" : "Missing")})", r.AppDataExists);
        PrintCheck("Tasks Database", r.TasksFileExists ? $"Found ({r.TasksCount} tasks)" : "No tasks.json (will create on first task)", true);
        PrintCheck("Notes Database", r.NotesFileExists ? $"Found ({r.NotesCount} notes)" : "No notes.json (will create on first note)", true);
        PrintCheck("Settings File", r.SettingsFileExists ? "Found" : "No settings.json (defaults in use)", true);
        PrintCheck("User PATH", r.UserPathConfigured ? "Configured" : "Not found in User PATH (restart shell or add bin/ to PATH)", r.UserPathConfigured);

        Console.WriteLine();
        if (r.Diagnostics.Count > 0)
        {
            Console.WriteLine("Diagnostics & Recommendations:");
            foreach (var diag in r.Diagnostics)
            {
                Console.WriteLine($"  * {diag}");
            }
        }
        else
        {
            Console.WriteLine("All system checks passed! Neku is healthy.");
        }
    }

    private static void PrintCheck(string label, string value, bool ok)
    {
        string status = ok ? "[ OK ]" : "[WARN]";
        Console.WriteLine($" {status} {label,-16} : {value}");
    }
}
