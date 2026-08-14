using ComplexityAnalyzer.Core;
using Xunit;

namespace ComplexityAnalyzer.Tests;

public class RecursionAndLinqTests
{
    [Fact]
    public void LinearRecursion_IsLinearTimeAndStack()
    {
        var result = SnippetAnalyzer.Analyze("""
            static int Sum(int n)
            {
                if (n <= 0) return 0;
                return n + Sum(n - 1);
            }
            """);
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
    }

    [Fact]
    public void MergeSortPattern_IsNLogN()
    {
        var result = SnippetAnalyzer.Analyze("""
            static void Sort(int[] a, int n)
            {
                if (n <= 1) return;
                Sort(a, n / 2);
                Sort(a, n / 2);
            }
            """);
        Assert.Equal("O(n log n)", ComplexityFormatter.FormatBigO(result.Time));
    }

    [Fact]
    public void MutualRecursion_IsUnresolved()
    {
        var result = SnippetAnalyzer.AnalyzeNamed("""
            using System;
            public static class Snippet
            {
                public static void A(int n) { if (n > 0) B(n); }
                public static void B(int n) { if (n > 0) A(n - 1); }
            }
            """, name: "A");
        Assert.True(
            result.Confidence <= AnalysisConfidence.Low
            || ComplexityFormatter.Format(result.Time).Contains("C("));
    }

    [Fact]
    public void Queryable_IsUnknown()
    {
        var result = SnippetAnalyzer.Analyze("""
            static int[] Load(IQueryable<int> rows) =>
                rows.Where(x => x > 0).ToArray();
            """);
        Assert.Equal("O(unknown)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal(AnalysisConfidence.Unknown, result.Confidence);
        Assert.Contains(
            result.Warnings,
            w => w.Message.Contains("database-side"));
    }

    [Fact]
    public void AsEnumerable_FlipsToInMemory()
    {
        var result = SnippetAnalyzer.Analyze("""
            static int[] Load(IQueryable<int> rows) =>
                rows.AsEnumerable().Where(x => x > 0).ToArray();
            """);
        // Source size of an IQueryable is still unknown, but the
        // operators after AsEnumerable are in-memory LINQ.
        Assert.DoesNotContain(
            "C(",
            ComplexityFormatter.Format(result.Time));
    }

    [Fact]
    public void UnboundedHeap_OffersBoundingSuggestion()
    {
        var result = SnippetAnalyzer.Analyze("""
            static void Collect(int[] values)
            {
                var pq = new PriorityQueue<int, int>();
                foreach (var value in values)
                    pq.Enqueue(value, value);
            }
            """);
        Assert.Equal("O(n log n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.NotEmpty(result.BoundingSuggestions);
        Assert.Contains(
            result.BoundingSuggestions,
            s => s.Condition.Contains("Count > k"));
    }

    [Fact]
    public void DeferredLinq_IsNotChargedUntilMaterialized()
    {
        var result = SnippetAnalyzer.Analyze("""
            static IEnumerable<int> Filter(int[] values) =>
                values.Where(x => x > 0);
            """);
        Assert.Equal("O(1)", ComplexityFormatter.FormatBigO(result.Time));
    }
}
