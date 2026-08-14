namespace ComplexityAnalyzer.Server;

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
