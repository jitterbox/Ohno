using System.IO.Pipes;
using ComplexityAnalyzer.Server;
using StreamJsonRpc;
using Xunit;

namespace ComplexityAnalyzer.Tests;

public class ServerProtocolTests
{
    [Fact]
    public void Initialize_ReturnsServerIdentity()
    {
        var service = new AnalyzerService();
        var result = service.Initialize();
        Assert.Equal("Ohno.ComplexityAnalyzer", result.ServerName);
        Assert.False(string.IsNullOrWhiteSpace(result.AnalyzerVersion));
    }

    [Fact]
    public void Analyze_ReturnsFunctionComplexity()
    {
        var service = new AnalyzerService();
        var response = service.Analyze(new AnalyzeRequest(
            "file:///tmp/Snippet.cs",
            """
            using System;
            public static class S
            {
                public static int GetFirst(int[] nums) => nums[0];
            }
            """,
            1,
            "fast"));

        Assert.Equal(1, response.Version);
        Assert.Contains(response.Functions, f => f.Name == "GetFirst");
        var fn = response.Functions.First(f => f.Name == "GetFirst");
        Assert.Equal("O(1)", fn.Time);
        Assert.Equal("O(1)", fn.Space);
        Assert.Equal("fast", fn.Tier);
        Assert.NotNull(fn.Evidence);
    }

    [Fact]
    public void Analyze_TopK_HasDimensionsAndEvidence()
    {
        var service = new AnalyzerService();
        var response = service.Analyze(new AnalyzeRequest(
            "file:///tmp/TopK.cs",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public static class S
            {
                public static int[] TopK(int[] values, int k)
                {
                    var pq = new PriorityQueue<int, int>();
                    foreach (var value in values)
                    {
                        pq.Enqueue(value, value);
                        if (pq.Count > k)
                            pq.Dequeue();
                    }
                    return pq.UnorderedItems.Select(x => x.Element).ToArray();
                }
            }
            """,
            2,
            "fast"));

        var fn = response.Functions.Single(f => f.Name == "TopK");
        Assert.Equal("O(n log k)", fn.Time);
        Assert.Equal("O(k)", fn.Space);
        Assert.Contains(fn.Dimensions, d => d.Variable == "n");
        Assert.Contains(fn.Dimensions, d => d.Variable == "k");
        Assert.NotEmpty(fn.Evidence.Children);
    }

    [Fact]
    public void AnalyzeDeep_WithoutSolution_UsesAdHocCompilation()
    {
        var service = new AnalyzerService();
        var response = service.AnalyzeDeep(new AnalyzeRequest(
            "file:///tmp/TopK.cs",
            """
            using System.Collections.Generic;
            using System.Linq;
            public static class S
            {
                public static int[] TopK(int[] values, int k)
                {
                    var pq = new PriorityQueue<int, int>();
                    foreach (var value in values)
                    {
                        pq.Enqueue(value, value);
                        if (pq.Count > k)
                            pq.Dequeue();
                    }
                    return pq.UnorderedItems.Select(x => x.Element).ToArray();
                }
            }
            """,
            1,
            "deep"));

        var fn = response.Functions.Single(f => f.Name == "TopK");
        Assert.Equal("O(n log k)", fn.Time);
        Assert.Equal("O(k)", fn.Space);
        Assert.Equal("deep", fn.Tier);
    }

    [Fact]
    public async Task JsonRpc_NamedObjectParams_MatchVscodeJsonRpc()
    {
        var serverToClient = new AnonymousPipeServerStream(PipeDirection.Out);
        var clientToServer = new AnonymousPipeServerStream(PipeDirection.Out);
        var serverIn = new AnonymousPipeClientStream(
            PipeDirection.In, clientToServer.GetClientHandleAsString());
        var clientIn = new AnonymousPipeClientStream(
            PipeDirection.In, serverToClient.GetClientHandleAsString());

        var service = new AnalyzerService();
        using var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverToClient, serverIn),
            service);
        service.Attach(serverRpc);
        serverRpc.StartListening();

        using var clientRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(clientToServer, clientIn));
        clientRpc.StartListening();

        var init = await clientRpc.InvokeAsync<InitializeResult>("initialize");
        Assert.Equal("Ohno.ComplexityAnalyzer", init.ServerName);

        var response = await clientRpc.InvokeWithParameterObjectAsync<AnalyzeResponse>(
            "ohno/analyze",
            new
            {
                uri = "file:///tmp/A.cs",
                text = "public static class S { public static int G(int[] n) => n[0]; }",
                version = 1,
                tier = "fast",
            });

        Assert.Contains(response.Functions, f => f.Name == "G");
    }
}
