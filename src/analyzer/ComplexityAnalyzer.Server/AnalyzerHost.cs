using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// JSON-RPC stdio host. Logs go to stderr only — stdout is the RPC stream.
/// </summary>
public sealed class AnalyzerHost
{
    public async Task<int> RunAsync()
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        var formatter = new JsonMessageFormatter();
        formatter.JsonSerializer.ContractResolver =
            new CamelCasePropertyNamesContractResolver();
        formatter.JsonSerializer.NullValueHandling = NullValueHandling.Ignore;
        var handler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);
        var service = new AnalyzerService();
        using var rpc = new JsonRpc(handler, service);
        service.Attach(rpc);
        rpc.StartListening();
        await rpc.Completion;
        return 0;
    }
}
