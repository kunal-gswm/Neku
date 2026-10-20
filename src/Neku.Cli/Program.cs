namespace Neku.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool jsonMode = false;
        var cleanArgs = new List<string>();

        foreach (string arg in args)
        {
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                jsonMode = true;
            }
            else
            {
                cleanArgs.Add(arg);
            }
        }

        int exitCode = await CommandRouter.RouteAsync(cleanArgs.ToArray(), jsonMode);
        Environment.ExitCode = exitCode;
        return exitCode;
    }
}
