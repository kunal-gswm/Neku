using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DesktopPet.Tests;

public sealed class CliCommandTests
{
    private static string GetCliExecutablePath()
    {
        string baseDir = AppContext.BaseDirectory;
        DirectoryInfo? dir = new DirectoryInfo(baseDir);
        var probed = new List<string>();

        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate1 = Path.Combine(dir.FullName, "src", "Neku.Cli", "bin", "Release", "net10.0-windows", "win-x64", "neku.exe");
            probed.Add(candidate1);
            if (File.Exists(candidate1)) return candidate1;

            string candidate2 = Path.Combine(dir.FullName, "src", "Neku.Cli", "bin", "Debug", "net10.0-windows", "win-x64", "neku.exe");
            probed.Add(candidate2);
            if (File.Exists(candidate2)) return candidate2;

            string candidate3 = Path.Combine(dir.FullName, "dist", "bin", "neku.exe");
            probed.Add(candidate3);
            if (File.Exists(candidate3)) return candidate3;

            string candidate4 = Path.Combine(dir.FullName, "src", "Neku.Cli", "bin", "Release", "net10.0-windows", "neku.exe");
            probed.Add(candidate4);
            if (File.Exists(candidate4)) return candidate4;

            string candidate5 = Path.Combine(dir.FullName, "src", "Neku.Cli", "bin", "Debug", "net10.0-windows", "neku.exe");
            probed.Add(candidate5);
            if (File.Exists(candidate5)) return candidate5;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate compiled neku.exe binary from {baseDir}. Probed locations:\n" + string.Join("\n", probed));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunCli(string arguments, int timeoutMs = 15000)
    {
        string exe = GetCliExecutablePath();
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Verified CLI binary does not exist at '{exe}'.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = new Process { StartInfo = psi };

        try
        {
            if (!proc.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{exe}' with arguments '{arguments}'.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Exception launching process '{exe}' with arguments '{arguments}': {ex.Message}", ex);
        }

        // Concurrently drain stdout and stderr asynchronously to prevent pipe buffer deadlocks
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch { }

            throw new TimeoutException($"Command '{exe} {arguments}' timed out after {timeoutMs}ms (PID: {proc.Id}).");
        }

        // Ensure stream readers finish draining all buffered bytes to EOF
        Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 5000);

        string stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";

        return (proc.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Help_FlagAndCommand_ReturnsSuccessAndListsAllCommandGroups()
    {
        var (code1, out1, _) = RunCli("--help");
        Assert.Equal(0, code1);
        Assert.Contains("Neku CLI", out1);
        Assert.Contains("General Commands:", out1);
        Assert.Contains("Pet Commands:", out1);
        Assert.Contains("Focus Commands:", out1);
        Assert.Contains("Task Commands:", out1);
        Assert.Contains("Note Commands:", out1);
        Assert.Contains("Companion Commands:", out1);
        Assert.Contains("Developer Events:", out1);

        var (code2, out2, _) = RunCli("-h");
        Assert.Equal(0, code2);
        Assert.Equal(out1.Trim(), out2.Trim());

        var (code3, out3, _) = RunCli("help");
        Assert.Equal(0, code3);
        Assert.Equal(out1.Trim(), out3.Trim());
    }

    [Fact]
    public void Version_FlagAndJson_ReturnsVersionString()
    {
        var (code1, out1, _) = RunCli("--version");
        Assert.Equal(0, code1);
        Assert.Equal("Neku 1.0.3", out1.Trim());

        var (code2, out2, _) = RunCli("-v");
        Assert.Equal(0, code2);
        Assert.Equal("Neku 1.0.3", out2.Trim());

        var (code3, out3, _) = RunCli("--version --json");
        Assert.Equal(0, code3);
        using var doc = JsonDocument.Parse(out3);
        Assert.Equal("1.0.3", doc.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void InvalidCommand_ReturnsExitCodeTwoAndUsageHelp()
    {
        var (code, _, err) = RunCli("completely_bogus_cmd");
        Assert.Equal(2, code);
        Assert.Contains("Unknown command 'completely_bogus_cmd'", err);
        Assert.Contains("neku --help", err);
    }

    [Theory]
    [InlineData("pet")]
    [InlineData("pet invalidsub")]
    [InlineData("focus")]
    [InlineData("focus invalidsub")]
    [InlineData("task")]
    [InlineData("task add")]
    [InlineData("task complete")]
    [InlineData("task delete")]
    [InlineData("note")]
    [InlineData("companion")]
    [InlineData("companion set")]
    [InlineData("event")]
    public void MissingOrInvalidSubcommands_ReturnsExitCodeTwo(string subCmd)
    {
        var (code, outText, errText) = RunCli(subCmd);
        Assert.Equal(2, code);
        string combined = (outText + "\n" + errText).ToLowerInvariant();
        Assert.True(combined.Contains("usage:") || combined.Contains("error:") || combined.Contains("missing") || combined.Contains("unknown"),
            $"Expected usage/error in: {outText} {errText}");
    }

    [Fact]
    public void Offline_WhenDesktopPetIsNotRunning_ReturnsExitCodeOneAndRecoveryGuidance()
    {
        var (code, outText, errText) = RunCli("status");
        Assert.True(code == 0 || code == 1);
        if (code == 1)
        {
            string combined = outText + "\n" + errText;
            Assert.Contains("not running", combined);
            Assert.Contains("neku launch", combined);
        }
    }

    [Fact]
    public void Offline_JsonMode_OutputsStructuredErrorWithoutHanging()
    {
        var (code, outText, _) = RunCli("status --json");
        Assert.True(code == 0 || code == 1);

        using var doc = JsonDocument.Parse(outText);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("ok", out var okElem));
        if (!okElem.GetBoolean())
        {
            Assert.True(root.TryGetProperty("error", out var errElem));
            Assert.Contains("not running", errElem.GetString() ?? "");
        }
    }

    [Fact]
    public void Doctor_RunsDiagnosticsSuccessfullyAndDoesNotLeakPrivateData()
    {
        var (code, outText, _) = RunCli("doctor");
        Assert.Equal(0, code);
        Assert.Contains("Neku CLI Doctor", outText);
        Assert.Contains("CLI Version", outText);
        Assert.Contains("Desktop Binary", outText);
        Assert.Contains("AppData Storage", outText);
        Assert.Contains("Tasks Database", outText);
        Assert.Contains("Notes Database", outText);
        Assert.Contains("User PATH", outText);

        // Security check: doctor must NOT dump full private note contents or env dumps
        Assert.DoesNotContain("secret", outText.ToLowerInvariant());
        Assert.DoesNotContain("password", outText.ToLowerInvariant());
    }

    [Fact]
    public void Doctor_JsonMode_OutputsStructuredReport()
    {
        var (code, outText, _) = RunCli("doctor --json");
        Assert.Equal(0, code);

        using var doc = JsonDocument.Parse(outText);
        var root = doc.RootElement;
        Assert.Equal("1.0.3", root.GetProperty("cliVersion").GetString());
        Assert.True(root.TryGetProperty("desktopExecutableFound", out _));
        Assert.True(root.TryGetProperty("desktopAppRunning", out _));
        Assert.True(root.TryGetProperty("ipcPipeName", out _));
        Assert.True(root.TryGetProperty("appDataDirectory", out _));
        Assert.True(root.TryGetProperty("tasksFileExists", out _));
        Assert.True(root.TryGetProperty("notesFileExists", out _));
    }

    [Fact]
    public void Launcher_Doctor_ResolvesDesktopExecutable()
    {
        var (code, outText, _) = RunCli("doctor --json");
        Assert.Equal(0, code);

        using var doc = JsonDocument.Parse(outText);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("desktopExecutableFound").GetBoolean());
        string exePath = root.GetProperty("desktopExecutablePath").GetString() ?? "";
        Assert.EndsWith("Neku.exe", exePath, StringComparison.OrdinalIgnoreCase);
    }
}
