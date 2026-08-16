using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// The class boundaries that get confused most often, from
/// <c>samples/roslyn/RoslynBoundaryBench.cs</c>.
/// </summary>
/// <remarks>
/// BigO(Bench) and CodeComplex both report that learned predictors
/// fail by sliding between <em>adjacent</em> classes — O(n) for
/// O(n log n), O(n log n) for O(n²). These pairs are written to look
/// alike and cost differently, so a regression that blurs the classes
/// changes an assertion instead of producing a plausible number.
/// </remarks>
public class BoundaryBenchTests
{
    private readonly ITestOutputHelper _output;

    public BoundaryBenchTests(ITestOutputHelper output) =>
        _output = output;

    public static TheoryData<string, string, string> Cases => new()
    {
        // Linear against linearithmic: same question, different method.
        { "HasDuplicateByHash", "O(n)", "O(n)" },
        { "HasDuplicateBySort", "O(n log n)", "O(n)" },
        { "LargestValue", "O(n)", "O(1)" },
        { "KthLargestBySort", "O(n log n)", "O(n)" },

        // Linearithmic against quadratic.
        { "InsertionSort", "O(n²)", "O(1)" },
        { "InsertionSortFor", "O(n²)", "O(1)" },
        { "LibrarySort", "O(n log n)", "O(1)" },
        { "CountInversionsNaive", "O(n²)", "O(1)" },

        // Logarithmic against linear.
        { "BinarySearchIndex", "O(log n)", "O(1)" },
        { "LinearSearchIndex", "O(n)", "O(1)" },

        // Independent dimensions must not collapse into one.
        { "SumOfBoth", "O(m + n)", "O(1)" },
        { "PairSum", "O(m n)", "O(1)" },

        // Heavier-looking than they are.
        { "BoundedInnerLoop", "O(n)", "O(1)" },
        { "HalvingWalk", "O(log k)", "O(1)" },
        { "TriangularSum", "O(n²)", "O(1)" },

        // Lighter-looking than they are.
        { "UniqueByListContains", "O(n²)", "O(n)" },
        { "SortedCopy", "O(n log n)", "O(n)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Boundary_MatchesKnownBound(
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
    /// The pairs, stated as pairs. Each asks the same question two
    /// ways, and the whole point is that the bounds differ.
    /// </summary>
    [Theory]
    [InlineData("HasDuplicateByHash", "HasDuplicateBySort")]
    [InlineData("LargestValue", "KthLargestBySort")]
    [InlineData("LibrarySort", "InsertionSort")]
    [InlineData("LibrarySort", "InsertionSortFor")]
    [InlineData("BinarySearchIndex", "LinearSearchIndex")]
    [InlineData("SumOfBoth", "PairSum")]
    public void AdjacentClasses_StayDistinct(string cheaper, string dearer)
    {
        var low = ComplexityFormatter.FormatBigO(Analyze(cheaper).Time);
        var high = ComplexityFormatter.FormatBigO(Analyze(dearer).Time);
        _output.WriteLine($"{cheaper} {low} vs {dearer} {high}");
        Assert.NotEqual(low, high);
    }

    /// <summary>
    /// A sort is the single easiest thing to lose: it hides behind one
    /// call, and losing it turns n log n into n.
    /// </summary>
    [Theory]
    [InlineData("HasDuplicateBySort")]
    [InlineData("KthLargestBySort")]
    [InlineData("LibrarySort")]
    [InlineData("SortedCopy")]
    public void SortingMethods_KeepTheirLogFactor(string name)
    {
        var time = ComplexityFormatter.FormatBigO(Analyze(name).Time);
        Assert.Contains("log", time, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two independent inputs stay two dimensions. Folding m into n
    /// would be a guess that m ≤ n.
    /// </summary>
    [Fact]
    public void IndependentInputs_StayIndependent()
    {
        var sum = ComplexityFormatter.FormatBigO(Analyze("SumOfBoth").Time);
        var product = ComplexityFormatter.FormatBigO(
            Analyze("PairSum").Time);
        Assert.Contains("m", sum, StringComparison.Ordinal);
        Assert.Contains("n", sum, StringComparison.Ordinal);
        Assert.Contains("m", product, StringComparison.Ordinal);
        Assert.Contains("n", product, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_CompilesWithoutErrors()
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "BoundaryBench");
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    private static ComplexityResult Analyze(string name)
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "BoundaryBench");
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
                "RoslynBoundaryBench.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("RoslynBoundaryBench.cs not found.");
    }
}
