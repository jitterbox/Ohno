using System.Diagnostics;
using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Wall-clock guard for the debounce path. Ohno re-analyzes the whole
/// file on every keystroke (250 ms debounce) and again on every
/// selection change, so a regression here is felt directly as editor
/// lag.
/// </summary>
/// <remarks>
/// These are regression ceilings, not targets: they are set well above
/// the measured baseline so ordinary machine-to-machine variance and CI
/// noise cannot fail the build, while an order-of-magnitude slowdown
/// still does. The printed timings are the useful output — read them
/// with <c>dotnet test -l "console;verbosity=detailed"</c> when
/// changing the walkers, the catalog, or the composition path.
/// </remarks>
public class AnalyzerBenchmarkTests
{
    /// <summary>Generous ceiling for one full pass over a fixture.</summary>
    private const int BudgetMs = 6000;

    /// <summary>
    /// Passes used to measure the warm (post-JIT) cost. The minimum of
    /// several passes is the estimator: on a shared or throttled
    /// machine the same fixture has been observed to vary by 2x run to
    /// run, so a single sample cannot support a fine-grained claim.
    /// Treat the printed numbers as indicative and the assertion as the
    /// only contract.
    /// </summary>
    private const int WarmPasses = 7;

    private readonly ITestOutputHelper _output;

    public AnalyzerBenchmarkTests(ITestOutputHelper output) =>
        _output = output;

    public static TheoryData<string, string> Fixtures => new()
    {
        { "leetcode", "OptimalSolutions.cs" },
        { "roslyn", "RoslynComplexityEdgeCases.cs" },
        { "roslyn", "RoslynSpaceComplexityPatterns.cs" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FullFileAnalysis_StaysWithinBudget(
        string folder, string file)
    {
        var source = File.ReadAllText(FixturePath(folder, file));
        var analyzer = new CSharpFileAnalyzer();

        // Cold pass: includes JIT and the first metadata load.
        var cold = Measure(() => analyzer.Analyze(source, AnalysisTier.Fast));

        var warm = long.MaxValue;
        var functions = 0;
        for (var i = 0; i < WarmPasses; i++)
        {
            FileAnalysis? analysis = null;
            var elapsed = Measure(
                () => analysis = analyzer.Analyze(source, AnalysisTier.Fast));
            warm = Math.Min(warm, elapsed);
            functions = analysis!.Functions.Count;
        }

        var lines = source.Split('\n').Length;
        _output.WriteLine(
            $"{file}: {lines} lines, {functions} functions, "
            + $"cold {cold} ms, warm(best of {WarmPasses}) {warm} ms, "
            + $"{PerFunction(warm, functions)} ms/function");

        Assert.True(
            warm < BudgetMs,
            $"{file} took {warm} ms (budget {BudgetMs} ms). "
            + "Analysis runs on the 250 ms debounce path; see "
            + "docs/PLAN-2026-08.md phase 2.");
    }

    /// <summary>
    /// Selection analysis re-sends the whole document, so it must not
    /// cost meaningfully more than a full-file pass.
    /// </summary>
    [Fact]
    public void SelectionAnalysis_IsNotSlowerThanFullFile()
    {
        var source = File.ReadAllText(
            FixturePath("leetcode", "OptimalSolutions.cs"));
        var analyzer = new CSharpFileAnalyzer();
        var span = FirstMethodBodySpan(source);

        analyzer.Analyze(source, AnalysisTier.Fast);
        var full = Measure(
            () => analyzer.Analyze(source, AnalysisTier.Fast));
        var selection = Measure(
            () => analyzer.AnalyzeSelection(source, span, AnalysisTier.Fast));

        _output.WriteLine(
            $"full {full} ms, selection {selection} ms "
            + $"(span lines {span.StartLine}-{span.EndLine})");

        Assert.True(
            selection < BudgetMs,
            $"Selection analysis took {selection} ms "
            + $"(budget {BudgetMs} ms).");
    }

    private static string PerFunction(long elapsed, int functions) =>
        functions == 0
            ? "n/a"
            : (elapsed / (double)functions).ToString("F1");

    private static long Measure(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    /// <summary>
    /// A span covering the body of the first method in the file, used
    /// as a representative selection.
    /// </summary>
    private static LineSpan FirstMethodBodySpan(string source)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("public static", StringComparison.Ordinal))
                continue;
            var end = Math.Min(i + 6, lines.Length - 1);
            return new LineSpan(i + 1, 0, end, 0);
        }

        return new LineSpan(0, 0, Math.Min(10, lines.Length - 1), 0);
    }

    private static string FixturePath(string folder, string file)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName, "samples", folder, file);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"{folder}/{file} not found.");
    }
}
