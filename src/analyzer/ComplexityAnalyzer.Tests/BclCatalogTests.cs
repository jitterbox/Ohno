using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Everyday BCL usage from <c>samples/roslyn/RoslynBclCatalog.cs</c>.
/// </summary>
/// <remarks>
/// The rest of the corpus only used members the catalog already knew,
/// so it could not catch the two failure modes this suite exists for:
/// an uncataloged member costed as O(1) (a sort silently erased), and
/// an uncataloged <c>string</c> member costed as <c>C(name)</c>
/// (confidence dragged to Low on ordinary code).
/// </remarks>
public class BclCatalogTests
{
    private readonly ITestOutputHelper _output;

    public BclCatalogTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, string, string> Cases => new()
    {
        { "SortWithComparer", "O(n log n)", "O(n)" },
        { "OrderShorthand", "O(n log n)", "O(n)" },
        { "SumWithSelector", "O(n)", "O(1)" },
        { "HeaviestItem", "O(n)", "O(1)" },
        { "SharedValues", "O(m + n)", "O(m + n)" },
        { "Combined", "O(m + n)", "O(m + n)" },
        { "UniqueValues", "O(n)", "O(n)" },
        { "SplitFields", "O(n)", "O(n)" },
        { "Tail", "O(n)", "O(n)" },
        { "Mentions", "O(n)", "O(1)" },
        { "Normalize", "O(n)", "O(n)" },
        { "JoinNames", "O(n)", "O(n)" },
        { "BuildReport", "O(n)", "O(n)" },
        { "FirstSeparator", "O(n)", "O(1)" },
        { "SortInPlace", "O(n log n)", "O(1)" },
        { "BuildLookup", "O(n)", "O(n)" },
        { "Reserved", "O(n)", "O(n)" },
        { "Duplicate", "O(n)", "O(n)" },
        { "FlipInPlace", "O(n)", "O(1)" },
        { "LazyEvens", "O(1)", "O(1)" },
        { "Clamp", "O(1)", "O(1)" },
        { "GlueThree", "O(m + n + p)", "O(m + n + p)" },
        { "AtList", "O(1)", "O(1)" },
        { "AtSorted", "O(log n)", "O(1)" },
        { "AtImmutable", "O(n)", "O(1)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void BclUsage_MatchesKnownBound(
        string name, string time, string space)
    {
        var result = Analyze(name);
        var actualTime = ComplexityFormatter.FormatBigO(result.Time);
        var actualSpace = ComplexityFormatter.FormatBigO(
            result.AuxiliarySpace);
        _output.WriteLine(
            $"{name}: {actualTime} / {actualSpace} "
            + $"(expected {time} / {space}) conf={result.Confidence}");
        Assert.Equal(time, actualTime);
        Assert.Equal(space, actualSpace);
    }

    /// <summary>
    /// The regression that motivated the catalog work: a comparer
    /// overload is still a sort. It must never collapse to a constant.
    /// </summary>
    [Theory]
    [InlineData("SortWithComparer")]
    [InlineData("OrderShorthand")]
    [InlineData("SortInPlace")]
    public void SortOverloads_AreNeverConstant(string name)
    {
        var time = ComplexityFormatter.FormatBigO(Analyze(name).Time);
        Assert.Contains("log", time, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ordinary string and LINQ code should not be littered with
    /// C(name) placeholders — that was the other half of the split
    /// behaviour the catalog fixes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void CatalogedUsage_LeavesNoOpaqueCall(
        string name, string time, string space)
    {
        _ = time;
        _ = space;
        var result = Analyze(name);
        var formatted = ComplexityFormatter.FormatBigO(result.Time);
        Assert.DoesNotContain("C(", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "unknown", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deferred LINQ is constant to build. The catalog must not turn
    /// the streaming operators into eager scans.
    /// </summary>
    [Fact]
    public void DeferredPipeline_StaysConstant()
    {
        var result = Analyze("LazyEvens");
        Assert.Equal(
            "O(1)", ComplexityFormatter.FormatBigO(result.Time));
    }

    [Fact]
    public void Fixture_CompilesWithoutErrors()
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "BclCatalog");
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    /// <summary>
    /// Indexing through an interface (or any cataloged indexer) must
    /// keep its bound — removing the blanket <c>get_Item</c> allowlist
    /// must not turn a list index into a dangling C(name).
    /// </summary>
    [Fact]
    public void InterfaceIndexer_KeepsConstantRead()
    {
        const string source = """
            using System.Collections.Generic;
            public static class Probe
            {
                public static int Sum(IReadOnlyList<int> values)
                {
                    var s = 0;
                    for (var i = 0; i < values.Count; i++) s += values[i];
                    return s;
                }
            }
            """;
        var analysis = new CSharpFileAnalyzer().Analyze(source, AnalysisTier.Fast);
        var fn = analysis.Functions.Single(f => f.Symbol.Name == "Sum");
        var time = ComplexityFormatter.FormatBigO(fn.Result.Time);
        _output.WriteLine($"Sum: {time} conf={fn.Result.Confidence}");
        Assert.Equal("O(n)", time);
        Assert.DoesNotContain("C(", time, StringComparison.Ordinal);
    }

    private static ComplexityResult Analyze(string name)
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "BclCatalog");
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
                "RoslynBclCatalog.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("RoslynBclCatalog.cs not found.");
    }
}
