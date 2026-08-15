using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Peak-space idioms and combinations from
/// <c>samples/roslyn/RoslynSpaceComplexityPatterns.cs</c>
/// and <c>RoslynSpaceComplexityCombinations.cs</c>.
/// Space is simultaneously retained memory, not allocation volume.
/// </summary>
public class SpacePatternTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "ConstantSpace", "O(n)", "O(1)" },
        { "LinearArray", "O(n)", "O(n)" },
        { "TwoIndependentArrays", "O(m + n)", "O(m + n)" },
        { "RectangularMatrix", "O(m n)", "O(m n)" },
        { "SquareMatrix", "O(n²)", "O(n²)" },
        { "CubicArray", "O(n³)", "O(n³)" },
        { "RepeatedButNotRetained", "O(n²)", "O(n)" },
        { "RepeatedAndRetained", "O(n²)", "O(n²)" },
        { "TopK", "O(n log k)", "O(k)" },
        // new Queue<int>(k) reserves k slots, which is Θ(k) work on top
        // of the Θ(n) scan. k and n are independent dimensions, so the
        // sum stands rather than assuming k <= n.
        { "SlidingWindow", "O(k + n)", "O(k)" },
        { "CountUnique", "O(n)", "O(n)" },
        { "BuildAdjacencyList", "O(m + n)", "O(m + n)" },
        { "BuildAdjacencyMatrix", "O(n²)", "O(n²)" },
        { "RecursiveBinarySearch", "O(log n)", "O(log n)" },
        { "LinearRecursion", "O(n)", "O(n)" },
        { "FibonacciNaive", "O(2^n)", "O(n)" },
        { "TwoDimensionalMemo", "O(m n)", "O(m n)" },
        { "RetainLinearDataPerLogLevel", "O(n log n)", "O(n log n)" },
        { "AllSubsets", "O(n 2^n)", "O(n 2^n)" },
        { "AllPermutations", "O(n n!)", "O(n n!)" },
        { "AllCombinations", "O(k C(n, k))", "O(k C(n, k))" },
        { "RepeatString", "O(k n)", "O(k n)" },
        { "BreadthFirstCount", "O(k n)", "O(n)" },
        { "DepthFirstCount", "O(k n)", "O(n)" },
    };

    public static TheoryData<string, string, string> Combinations => new()
    {
        { "ComboMatrixAndLinear", "O(n²)", "O(n²)" },
        { "ComboPeakThenRetain", "O(n²)", "O(n²)" },
        { "ComboWindowAndUnique", "O(k + n)", "O(k + n)" },
        { "ComboBufferAndLinearRecursion", "O(n)", "O(n)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SpacePattern_Matches(string name, string time, string space)
    {
        var result = Analyze(
            Fixture("RoslynSpaceComplexityPatterns.cs"), name);
        Assert.Equal(time, ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal(
            space, ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
    }

    [Fact]
    public void HighConfidence_HasNoReasons()
    {
        var result = Analyze(
            Fixture("RoslynSpaceComplexityPatterns.cs"), "ConstantSpace");
        Assert.Equal(AnalysisConfidence.High, result.Confidence);
        Assert.Empty(result.ConfidenceReasons);
    }

    [Fact]
    public void IdiomMatch_IsMediumWithReason()
    {
        var window = Analyze(
            Fixture("RoslynSpaceComplexityPatterns.cs"), "SlidingWindow");
        Assert.Equal(AnalysisConfidence.Medium, window.Confidence);
        Assert.Contains(
            window.ConfidenceReasons,
            r => r.Contains("Count > k", StringComparison.Ordinal));

        var fib = Analyze(
            Fixture("RoslynSpaceComplexityPatterns.cs"), "FibonacciNaive");
        Assert.Equal(AnalysisConfidence.Medium, fib.Confidence);
        Assert.Contains(
            fib.ConfidenceReasons,
            r => r.Contains("Recurrence classified", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Combination_Composes(string name, string time, string space)
    {
        var result = Analyze(
            Fixture("RoslynSpaceComplexityCombinations.cs"), name);
        Assert.Equal(time, ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal(
            space, ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
    }

    private static ComplexityResult Analyze(string path, string name)
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(path), "SpacePatterns");
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

    private static string Fixture(string file)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName, "samples", "roslyn", file);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(file);
    }
}
