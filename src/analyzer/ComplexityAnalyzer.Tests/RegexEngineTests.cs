using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// The two regex engines, from
/// <c>samples/roslyn/RoslynRegexEngines.cs</c>.
/// </summary>
/// <remarks>
/// This is the one place the honesty rule points toward a *tighter*
/// bound. Backtracking cost depends on the pattern and can explode, so
/// it stays opaque. <c>NonBacktracking</c> carries a documented
/// linear-time guarantee from the runtime, so reporting O(unknown)
/// there is needlessly pessimistic rather than careful.
/// </remarks>
public class RegexEngineTests
{
    private readonly ITestOutputHelper _output;

    public RegexEngineTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("LinearMatch", "O(n)")]
    [InlineData("LinearWithCombinedOptions", "O(n)")]
    [InlineData("LinearStaticMatch", "O(n)")]
    [InlineData("LinearInlineMatch", "O(n)")]
    [InlineData("LinearReplace", "O(n)")]
    public void NonBacktracking_EarnsALinearBound(
        string name, string time)
    {
        var result = Analyze(name);
        var actual = ComplexityFormatter.FormatBigO(result.Time);
        _output.WriteLine($"{name}: {actual} conf={result.Confidence}");
        Assert.Equal(time, actual);
    }

    [Fact]
    public void MaterializingCall_RetainsItsOutput()
    {
        var result = Analyze("LinearReplace");
        Assert.Equal(
            "O(n)",
            ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
    }

    [Theory]
    [InlineData("BacktrackingMatch")]
    [InlineData("ProvidedRegex")]
    [InlineData("ExplicitBacktracking")]
    public void BacktrackingOrUnprovable_StaysOpaque(string name)
    {
        var result = Analyze(name);
        var actual = ComplexityFormatter.FormatBigO(result.Time);
        _output.WriteLine($"{name}: {actual}");
        Assert.Equal("O(unknown)", actual);
    }

    /// <summary>
    /// The linear result is an engine guarantee, not a proof about the
    /// pattern, so it is named and capped below High.
    /// </summary>
    [Fact]
    public void LinearMatch_IsNamedAndNotClaimedAsCertain()
    {
        var result = Analyze("LinearMatch");
        Assert.Contains(result.Patterns, p => p.Id == "regex-linear");
        Assert.DoesNotContain(result.Patterns, p => p.Id == "regex");
        Assert.NotEqual(AnalysisConfidence.High, result.Confidence);
        Assert.NotEmpty(result.ConfidenceReasons);
    }

    [Fact]
    public void BacktrackingMatch_KeepsTheOpaquePattern()
    {
        var result = Analyze("BacktrackingMatch");
        Assert.Contains(result.Patterns, p => p.Id == "regex");
    }

    [Fact]
    public void Fixture_CompilesWithoutErrors()
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "RegexEngines");
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    private static ComplexityResult Analyze(string name)
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "RegexEngines");
        var tree = CompilationFactory.SourceTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == name);
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        return new CSharpMethodAnalyzer()
            .Analyze(symbol, model, AnalysisTier.Fast);
    }

    private static string FixturePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "samples",
                "roslyn",
                "RoslynRegexEngines.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("RoslynRegexEngines.cs not found.");
    }
}
