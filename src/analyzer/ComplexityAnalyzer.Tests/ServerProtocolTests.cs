using System.IO.Pipes;
using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using ComplexityAnalyzer.Server;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    public async Task Analyze_ReturnsFunctionComplexity()
    {
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
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
    public async Task Analyze_TopK_HasDimensionsAndEvidence()
    {
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
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
    public async Task Analyze_DeferredLinq_ReturnsApproaches()
    {
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
            "file:///tmp/Linq.cs",
            """
            using System.Collections.Generic;
            using System.Linq;
            public static class S
            {
                public static IEnumerable<int> Positive(
                    IEnumerable<int> source)
                {
                    return source.Where(x => x > 0);
                }
            }
            """,
            1,
            "fast"));

        var fn = response.Functions.Single(f => f.Name == "Positive");
        Assert.Contains(fn.Approaches, a => a.Id == "deferred-linq");
        Assert.Contains(fn.Approaches, a => a.Role == "alternative");
        Assert.False(string.IsNullOrWhiteSpace(fn.SelectionHint));
    }

    [Fact]
    public async Task Analyze_Selection_ReturnsSyntheticMethod()
    {
        var text = """
            using System;
            public static class S
            {
                public static int Nested(int[] a, int[] b)
                {
                    var sum = 0;
                    foreach (var x in a)
                        foreach (var y in b)
                            sum += x * y;
                    return sum;
                }
            }
            """;
        var inner = SelectionSpan(text);
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
            "file:///tmp/Sel.cs",
            text,
            1,
            "fast",
            new RangeDto(
                inner.StartLine,
                inner.StartCharacter,
                inner.EndLine,
                inner.EndCharacter)));

        var fn = Assert.Single(response.Functions);
        Assert.EndsWith("(selection)", fn.Name);
        Assert.Equal("O(m)", fn.Time);
    }

    private static LineSpan SelectionSpan(string source)
    {
        var tree = CompilationFactory.SourceTree(
            CompilationFactory.Create(source, "Sel"));
        var node = tree.GetRoot()
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Last();
        var loc = node.GetLocation().GetLineSpan();
        return new LineSpan(
            loc.StartLinePosition.Line,
            loc.StartLinePosition.Character,
            loc.EndLinePosition.Line,
            loc.EndLinePosition.Character);
    }

    [Fact]
    public async Task AnalyzeDeep_WithoutSolution_UsesAdHocCompilation()
    {
        var service = new AnalyzerService();
        var response = await service.AnalyzeDeep(new AnalyzeRequest(
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
