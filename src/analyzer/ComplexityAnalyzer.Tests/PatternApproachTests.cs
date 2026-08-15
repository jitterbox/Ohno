using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.Tests;

public class PatternApproachTests
{
    [Fact]
    public void AwaitBesideLoop_KeepsLinearBound()
    {
        var result = SnippetAnalyzer.AnalyzeNamed(
            """
            using System.Threading.Tasks;
            public static class Snippet
            {
                public static async Task<int> SumAsync(int[] a)
                {
                    await Task.Yield();
                    var sum = 0;
                    foreach (var x in a) sum += x;
                    return sum;
                }
            }
            """,
            name: "SumAsync");
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Contains(result.Patterns, p => p.Id == "await-opaque");
        Assert.Equal(
            PatternEffect.Annotate,
            result.Patterns.First(p => p.Id == "await-opaque").Effect);
        Assert.NotEqual(AnalysisConfidence.Unknown, result.Confidence);
    }

    [Fact]
    public void PureAwait_StaysUnknown()
    {
        var result = SnippetAnalyzer.AnalyzeNamed(
            """
            using System;
            using System.Threading.Tasks;
            public static class Snippet
            {
                public static async Task<int> JustAwait(
                    Func<Task<int>> work)
                {
                    return await work();
                }
            }
            """,
            name: "JustAwait");
        Assert.Equal(
            "O(unknown)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Contains(result.Patterns, p => p.Id == "await-opaque");
    }

    [Fact]
    public void BinarySearch_IsNamedNotDataDependent()
    {
        var result = SnippetAnalyzer.Analyze("""
            public static int Find(int[] a, int lo, int hi, int t)
            {
                if (lo > hi) return -1;
                var mid = (lo + hi) / 2;
                if (a[mid] == t) return mid;
                if (a[mid] < t) return Find(a, mid + 1, hi, t);
                return Find(a, lo, mid - 1, t);
            }
            """);
        Assert.Equal("O(log n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Contains(result.Patterns, p => p.Id == "binary-search");
        Assert.DoesNotContain(
            result.Patterns, p => p.Id == "data-dependent-recursion");
        Assert.Contains(result.Approaches, a => a.Id == "binary-search");
    }

    [Fact]
    public void BoundedRecursion_IsAnAlternative()
    {
        var result = SnippetAnalyzer.Analyze("""
            public static int Fib(int n, int max)
            {
                if (max <= 0) return 0;
                if (n <= 1) return n;
                return Fib(n - 1, max) + Fib(n - 2, max);
            }
            """);
        Assert.Equal("O(2^n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Contains(result.Patterns, p => p.Id == "bounded-recursion");
        Assert.Contains(result.Approaches, a => a.Id == "bounded-recursion");
        Assert.Contains(result.Approaches, a => a.Role == "alternative");
        Assert.False(string.IsNullOrEmpty(result.SelectionHint));
    }

    [Fact]
    public void DeferredLinq_OffersEnumerationAlternative()
    {
        var result = SnippetAnalyzer.Analyze("""
            public static IEnumerable<int> Positive(IEnumerable<int> src)
            {
                return src.Where(x => x > 0);
            }
            """);
        Assert.Contains(result.Approaches, a => a.Id == "deferred-linq");
        Assert.Contains(result.Approaches, a => a.Id == "deferred-linq-enum");
        Assert.False(string.IsNullOrEmpty(result.SelectionHint));
    }

    [Fact]
    public void DataDependentRecursion_ListsBothEnds()
    {
        var result = SnippetAnalyzer.Analyze("""
            public static int Walk(int[] values, int index)
            {
                if (index >= values.Length) return 0;
                if (values[index] > 0)
                {
                    return 1 + Walk(values, index + 1)
                        + Walk(values, index + 1);
                }
                return 1 + Walk(values, index + 1);
            }
            """);
        Assert.Contains(
            result.Approaches, a => a.Id == "data-dependent-recursion");
        Assert.Contains(result.Approaches, a => a.Id == "single-branch");
        Assert.True(result.Approaches.Count is >= 2 and <= 3);
    }

    [Fact]
    public void ParameterCountdown_IsAnnotated()
    {
        var result = SnippetAnalyzer.Analyze("""
            public static int CountDown(int n)
            {
                var steps = 0;
                while (n > 0)
                {
                    n--;
                    steps++;
                }
                return steps;
            }
            """);
        Assert.Contains(result.Patterns, p => p.Id == "numeric-countdown");
    }
}
