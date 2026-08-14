using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Cardinality and catalog gaps from
/// <c>samples/roslyn/RoslynCardinalityGaps.cs</c>.
/// </summary>
public class CardinalityGapTests
{
    private readonly ITestOutputHelper _output;

    public CardinalityGapTests(ITestOutputHelper output) =>
        _output = output;

    public static TheoryData<string, string, string> Cases => new()
    {
        { "Huffman", "O(n log n)", "O(n)" },
        { "RunningMedian", "O(n log n)", "O(n)" },
        { "StackDepthFirstCount", "O(k n)", "O(n)" },
        { "BfsNoVisited", "O(unknown)", "O(unknown)" },
        { "WindowRemoveAt", "O(k n)", "O(k)" },
        { "WindowTryDequeue", "O(n)", "O(k)" },
        { "HeapifyFromEnumerable", "O(n)", "O(n)" },
        { "SortedSetInsert", "O(n log n)", "O(n)" },
        { "StringBuilderJoin", "O(n)", "O(n)" },
        { "ImmutableListBuild", "O(n log n)", "O(n)" },
        { "SpanScan", "O(n)", "O(1)" },
        { "CollectionSpread", "O(m + n)", "O(m + n)" },
        { "HalvingShift", "O(log n)", "O(1)" },
        { "UnreachableEnqueue", "O(1)", "O(1)" },
        { "LoopIndexNotEmitted", "O(n²)", "O(1)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Gap_MatchesKnownBound(
        string name, string time, string space)
    {
        var result = Analyze(name);
        var actualTime = ComplexityFormatter.FormatBigO(result.Time);
        var actualSpace = ComplexityFormatter.FormatBigO(
            result.AuxiliarySpace);
        _output.WriteLine(
            $"{name}: {actualTime} / {actualSpace} " +
            $"(expected {time} / {space})");
        Assert.Equal(time, actualTime);
        Assert.Equal(space, actualSpace);
    }

    [Fact]
    public void LoopIndex_IsNotAComplexityVariable()
    {
        var result = Analyze("LoopIndexNotEmitted");
        var time = ComplexityFormatter.FormatBigO(result.Time);
        Assert.DoesNotContain("i", time, StringComparison.Ordinal);
        Assert.DoesNotContain("j", time, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_CompilesWithoutErrors()
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "CardinalityGaps");
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    private static ComplexityResult Analyze(string name)
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "CardinalityGaps");
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
                "RoslynCardinalityGaps.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(
            "RoslynCardinalityGaps.cs not found.");
    }
}
