namespace ComplexityAnalyzer.Server;

/// <summary>
/// Stdio host for the Ohno analyzer. Speaks JSON-RPC with the VS Code
/// extension. Fast analysis uses an ad-hoc compilation; deep analysis
/// uses <c>MSBuildWorkspace</c> when a solution path is set.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.Error.WriteLine("Ohno.ComplexityAnalyzer 0.1.0");
            return 0;
        }

        var host = new AnalyzerHost();
        return await host.RunAsync();
    }
}
