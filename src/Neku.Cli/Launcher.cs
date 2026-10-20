using System.Diagnostics;
using System.IO;
using Neku.Protocol;
using Microsoft.Win32;

namespace Neku.Cli;

public sealed record NekuLaunchResult(bool Success, string Message, string? ExecutablePath = null, bool AlreadyRunning = false);

public static class Launcher
{
    /// <summary>
    /// Searches for the Neku.exe executable across standard install paths, portable relative paths, and registry.
    /// </summary>
    public static string? ResolveNekuExecutable()
    {
        var candidates = new List<string>();

        // 1. Sibling of bin folder (installed layout: %LocalAppData%\Programs\Neku\bin\neku.exe -> %LocalAppData%\Programs\Neku\Neku.exe)
        try
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "Neku.exe")));
            candidates.Add(Path.Combine(baseDir, "Neku.exe"));
        }
        catch { }

        // 2. Standard per-user install directory
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Programs", "Neku", "Neku.exe"));
        }

        // 3. Inno Setup Uninstall Registry entry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5D9D49C7-9D19-4C79-99E7-3E5A47C7B1F2}_is1");
            if (key != null)
            {
                var installLoc = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(installLoc))
                {
                    candidates.Add(Path.Combine(installLoc, "Neku.exe"));
                }
            }
        }
        catch { }

        // 4. Relative dev build output / dist directory
        try
        {
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                candidates.Add(Path.Combine(dir.FullName, "dist", "Neku.exe"));
                candidates.Add(Path.Combine(dir.FullName, "src", "DesktopPet", "bin", "Release", "net10.0-windows", "win-x64", "Neku.exe"));
                candidates.Add(Path.Combine(dir.FullName, "src", "DesktopPet", "bin", "Debug", "net10.0-windows", "win-x64", "Neku.exe"));
                dir = dir.Parent;
            }
        }
        catch { }

        // 5. System PATH
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathEnv))
            {
                foreach (string entry in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    candidates.Add(Path.Combine(entry, "Neku.exe"));
                }
            }
        }
        catch { }

        foreach (string candidate in candidates.Distinct())
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Launches the desktop companion if not running, or activates/unhides it if already running.
    /// </summary>
    public static async Task<NekuLaunchResult> LaunchOrActivateAsync()
    {
        // 1. Check if already running via IPC ping
        try
        {
            var ping = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Ping, timeoutMs: 400);
            if (ping.Ok)
            {
                // Unhide / activate
                await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.PetShow, timeoutMs: 500);
                return new NekuLaunchResult(true, "Neku is already running (brought to front).", null, AlreadyRunning: true);
            }
        }
        catch { }

        // 2. Resolve executable
        string? exePath = ResolveNekuExecutable();
        if (exePath == null)
        {
            return new NekuLaunchResult(
                false,
                "Could not locate Neku.exe.\nPlease ensure Neku is installed or run the executable directly from its installation directory.");
        }

        // 3. Launch process
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return new NekuLaunchResult(false, $"Failed to start process '{exePath}'.", exePath);
            }

            // Wait briefly to confirm startup
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(250);
                try
                {
                    var ping = await IpcClient.SendCommandAsync(NekuIpcProtocol.Commands.Ping, timeoutMs: 300);
                    if (ping.Ok)
                    {
                        return new NekuLaunchResult(true, $"Launched Neku desktop companion ({Path.GetFileName(exePath)}).", exePath);
                    }
                }
                catch { }
            }

            return new NekuLaunchResult(true, $"Started Neku desktop companion process ({Path.GetFileName(exePath)}).", exePath);
        }
        catch (Exception ex)
        {
            return new NekuLaunchResult(false, $"Error launching Neku: {ex.Message}", exePath);
        }
    }
}
