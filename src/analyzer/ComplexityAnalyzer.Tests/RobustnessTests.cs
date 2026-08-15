using System.Text;
using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Inputs that should degrade rather than take the server down.
/// </summary>
/// <remarks>
/// The analyzer runs in a long-lived stdio process shared by the whole
/// editor session. A stack overflow there is not a failed analysis —
/// it kills the process, and with it the loaded workspace. Generated
/// and machine-written C# routinely contains expressions far deeper
/// than anything hand-written, so the walker must have a floor.
/// </remarks>
public class RobustnessTests
{
    private readonly ITestOutputHelper _output;

    public RobustnessTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(500)]
    [InlineData(5_000)]
    public void DeeplyChainedExpression_DoesNotCrash(int terms)
    {
        var source = MethodWith(
            "var total = 0" + Repeat(" + 1", terms) + ";\nreturn total;");

        var result = Analyze(source);

        _output.WriteLine(
            $"{terms} terms: {ComplexityFormatter.FormatBigO(result.Time)} "
            + $"conf={result.Confidence}");
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(20_000)]
    public void WideArrayInitializer_DoesNotCrash(int elements)
    {
        var values = new StringBuilder();
        for (var i = 0; i < elements; i++)
        {
            if (i > 0) values.Append(", ");
            values.Append(i % 97);
        }

        var source = MethodWith(
            $"var data = new[] {{ {values} }};\nreturn data.Length;");

        var result = Analyze(source);

        _output.WriteLine(
            $"{elements} elements: "
            + ComplexityFormatter.FormatBigO(result.Time));
        Assert.NotNull(result);
    }

    /// <summary>
    /// The depth cap is a floor against generated code, not a budget
    /// ordinary code can hit. Hand-written C# nests operations by tens,
    /// not hundreds — this pins that the limit stays well clear of it.
    /// </summary>
    [Fact]
    public void OrdinaryNesting_IsStillAnalyzedNormally()
    {
        var source = MethodWith(
            "var total = 0" + Repeat(" + 1", 100) + ";\n"
            + "foreach (var value in values)\n"
            + "    total += value;\n"
            + "return total;");

        var result = Analyze(source);

        Assert.Equal(
            "O(n)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.NotEqual(AnalysisConfidence.Unknown, result.Confidence);
    }

    [Fact]
    public void DeeplyNestedBlocks_ReportUnknownRatherThanConstant()
    {
        // Past the walk limit the honest answer is "not examined",
        // never a constant: unexamined work has not been shown free.
        const int depth = 600;
        var body = new StringBuilder();
        for (var i = 0; i < depth; i++) body.Append("{ ");
        body.Append("var x = 1;");
        for (var i = 0; i < depth; i++) body.Append(" }");
        body.Append("\nreturn 0;");

        var result = Analyze(MethodWith(body.ToString()));
        var time = ComplexityFormatter.FormatBigO(result.Time);

        _output.WriteLine($"depth {depth}: {time} conf={result.Confidence}");
        Assert.Equal("O(unknown)", time);
        Assert.Equal(AnalysisConfidence.Unknown, result.Confidence);
    }

    [Fact]
    public void CancelledAnalysis_StopsInsteadOfFinishing()
    {
        var source = File.ReadAllText(FixturePath());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new CSharpFileAnalyzer().Analyze(
                source, AnalysisTier.Fast, cancelled.Token));
    }

    /// <summary>
    /// A token cancelled partway must stop the walk inside a single
    /// method too, not only between methods.
    /// </summary>
    [Fact]
    public void CancellationReachesInsideOneMethod()
    {
        var source = MethodWith(
            "var total = 0;\n"
            + "for (var i = 0; i < values.Length; i++)\n"
            + "    for (var j = 0; j < values.Length; j++)\n"
            + "        total += values[i] * values[j];\n"
            + "return total;");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new CSharpFileAnalyzer().Analyze(
                source, AnalysisTier.Fast, cancelled.Token));
    }

    private static ComplexityResult Analyze(string source)
    {
        var analysis = new CSharpFileAnalyzer()
            .Analyze(source, AnalysisTier.Fast);
        return analysis.Functions.Single().Result;
    }

    private static string MethodWith(string body) =>
        $$"""
        public static class Generated
        {
            public static int Run(int[] values)
            {
        {{body}}
            }
        }
        """;

    private static string Repeat(string text, int times) =>
        string.Concat(Enumerable.Repeat(text, times));

    private static string FixturePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "samples",
                "leetcode",
                "OptimalSolutions.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("OptimalSolutions.cs not found.");
    }
}
