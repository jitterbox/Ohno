using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.Tests;

public class SelectionAnalysisTests
{
    [Fact]
    public void InnerLoop_DropsOuterBound()
    {
        var source = """
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
        var inner = LastForeach(source);
        var result = new CSharpFileAnalyzer()
            .AnalyzeSelection(source, inner, AnalysisTier.Fast)
            .Functions.Single()
            .Result;
        Assert.Equal("O(m)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.DoesNotContain("n * m", ComplexityFormatter.Format(result.Time));
    }

    [Fact]
    public void TwoLoops_HintToNarrow()
    {
        var source = """
            using System;
            public static class S
            {
                public static int Both(int[] a, int[] b)
                {
                    var sum = 0;
                    foreach (var x in a) sum += x;
                    foreach (var y in b) sum += y;
                    return sum;
                }
            }
            """;
        var span = CoverForeaches(source);
        var result = new CSharpFileAnalyzer()
            .AnalyzeSelection(source, span, AnalysisTier.Fast)
            .Functions.Single()
            .Result;
        Assert.False(string.IsNullOrEmpty(result.SelectionHint));
        Assert.Contains("Narrow the selection", result.SelectionHint);
        Assert.True(result.Approaches.Count is >= 2 and <= 3);
    }

    [Fact]
    public void OutsideMethod_IsEmpty()
    {
        var source = """
            using System;
            public static class S
            {
                public static int G(int[] n) => n[0];
            }
            """;
        var span = new LineSpan(0, 0, 0, 5);
        var analysis = new CSharpFileAnalyzer()
            .AnalyzeSelection(source, span, AnalysisTier.Fast);
        Assert.Empty(analysis.Functions);
        Assert.Contains(
            analysis.Warnings,
            w => w.Message.Contains("inside a method"));
    }

    private static LineSpan LastForeach(string source)
    {
        var tree = CompilationFactory.SourceTree(
            CompilationFactory.Create(source, "Sel"));
        var node = tree.GetRoot()
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Last();
        return SpanOf(node);
    }

    private static LineSpan CoverForeaches(string source)
    {
        var tree = CompilationFactory.SourceTree(
            CompilationFactory.Create(source, "Sel"));
        var loops = tree.GetRoot()
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .ToArray();
        var first = SpanOf(loops[0]);
        var last = SpanOf(loops[^1]);
        return new LineSpan(
            first.StartLine,
            first.StartCharacter,
            last.EndLine,
            last.EndCharacter);
    }

    private static LineSpan SpanOf(ForEachStatementSyntax node)
    {
        var loc = node.GetLocation().GetLineSpan();
        return new LineSpan(
            loc.StartLinePosition.Line,
            loc.StartLinePosition.Character,
            loc.EndLinePosition.Line,
            loc.EndLinePosition.Character);
    }
}
