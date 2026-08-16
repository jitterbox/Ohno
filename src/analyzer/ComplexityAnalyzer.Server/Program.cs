namespace ComplexityAnalyzer.Server;

/// <summary>
/// Stdio host for the Ohno analyzer. Speaks JSON-RPC with the VS Code
/// extension. Fast analysis uses the project compilation when ready,
/// otherwise an ad-hoc compilation; deep waits on
/// <c>MSBuildWorkspace</c> when a solution path is set.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.Error.WriteLine("Ohno.ComplexityAnalyzer 0.1.4");
            return 0;
        }

        var host = new AnalyzerHost();
        return await host.RunAsync();
    }
}
