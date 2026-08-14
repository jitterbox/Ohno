using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using ComplexityAnalyzer.Server;

namespace ComplexityAnalyzer.Tests;

public class CompilationContextTests
{
    [Fact]
    public void TopLevelStatements_AreAnnotatedAsMain()
    {
        var analysis = new CSharpFileAnalyzer().Analyze(
            """
            foreach (var item in args)
                System.Console.Write(item);
            """,
            AnalysisTier.Fast);
        var main = Assert.Single(analysis.Functions);
        Assert.Equal("<Main>$", main.Symbol.Name);
        Assert.Equal(
            "O(n)",
            ComplexityFormatter.FormatBigO(main.Result.Time));
    }

    [Fact]
    public void PrimaryConstructor_IsADimension()
    {
        var result = SnippetAnalyzer.AnalyzeNamed(
            """
            class Solution(int[] nums)
            {
                public int Sum()
                {
                    var total = 0;
                    foreach (var item in nums)
                        total += item;
                    return total;
                }
            }
            """,
            name: "Sum");
        Assert.Equal(
            "O(n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Contains(result.Dimensions, d => d.Variable == "n");
    }

    [Fact]
    public void LocalFunctions_AreNotListed()
    {
        var analysis = new CSharpFileAnalyzer().Analyze(
            """
            public static class S
            {
                public static int Outer(int[] values)
                {
                    int Inner()
                    {
                        var total = 0;
                        foreach (var item in values)
                            total += item;
                        return total;
                    }
                    return Inner();
                }
            }
            """,
            AnalysisTier.Fast);
        Assert.DoesNotContain(
            analysis.Functions, f => f.Symbol.Name == "Inner");
        var outer = Assert.Single(
            analysis.Functions, f => f.Symbol.Name == "Outer");
        Assert.Equal(
            "O(n)",
            ComplexityFormatter.FormatBigO(outer.Result.Time));
    }

    [Fact]
    public void UnresolvedType_ProducesWarning()
    {
        var analysis = new CSharpFileAnalyzer().Analyze(
            """
            public static class S
            {
                public static void F(WebApplication app) { }
            }
            """,
            AnalysisTier.Fast);
        Assert.Contains(
            analysis.Warnings,
            w => w.Message.Contains("WebApplication"));
    }

    [Fact]
    public async Task FileBasedPackage_ProducesWarning()
    {
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
            "file:///tmp/app.cs",
            """
            #:package Newtonsoft.Json@13.0.3
            foreach (var item in args)
                System.Console.Write(item);
            """,
            1,
            "fast"));
        Assert.Contains(response.Functions, f => f.Name == "Main");
        Assert.Contains(
            response.Warnings,
            w => w.Message.Contains("#:package"));
    }
}
