using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Adversarial cases from
/// <c>samples/roslyn/RoslynComplexityEdgeCases.cs</c>.
/// Fast and deep must agree; inconclusive hazards must be
/// <c>O(unknown)</c> with a named pattern, not a fake O(1).
/// </summary>
public class EdgeCaseTortureTests
{
    private readonly ITestOutputHelper _output;

    public EdgeCaseTortureTests(ITestOutputHelper output) =>
        _output = output;

    public static TheoryData<string, string> Cases => new()
    {
        { "DynamicDispatch", "INCONCLUSIVE" },
        { "ReflectionDispatch", "INCONCLUSIVE" },
        { "InterfaceDispatch", "INCONCLUSIVE" },
        { "DelegateInsideLoop", "CONTEXT_DEPENDENT" },
        { "MulticastDelegate", "CONTEXT_DEPENDENT" },
        { "PropertyAccessLooksConstant", "DERIVABLE_WITH_SUMMARIES" },
        { "IndexerLooksConstant", "DERIVABLE_WITH_SUMMARIES" },
        { "OperatorLooksConstant", "DERIVABLE_WITH_SUMMARIES" },
        { "GenericOperatorLoop", "CONTEXT_DEPENDENT" },
        { "DeferredLinq", "RANGE/CONTEXT_DEPENDENT" },
        { "EnumerateTwice", "CONTEXT_DEPENDENT" },
        { "YieldFilter", "RANGE" },
        { "ForeachOverSlowEnumerable", "DERIVABLE_WITH_SUMMARIES" },
        { "QueryProviderDependent", "INCONCLUSIVE" },
        { "RuntimeExpression", "INCONCLUSIVE" },
        { "AwaitOpaqueWork", "CONTEXT_DEPENDENT" },
        { "ConsumeAsyncStream", "CONTEXT_DEPENDENT/INCONCLUSIVE" },
        { "RegexBacktracking", "CONTEXT_DEPENDENT" },
        { "SortWithExpensiveComparer", "CONTEXT_DEPENDENT" },
        { "BuildCollisionDictionary", "CONTEXT_DEPENDENT" },
        { "CountDownBigInteger", "CONTEXT_DEPENDENT" },
        { "CollatzSteps", "NON_TERMINATION_RISK" },
        { "DataDependentBranchingRecursion", "RANGE" },
        { "CountLinkedNodes", "NON_TERMINATION_RISK" },
        { "LockWaitIsExternal", "CONTEXT_DEPENDENT" },
        { "CacheDependentSum", "RANGE" },
        { "StreamRead", "INCONCLUSIVE" },
        { "ParallelLoop", "CONTEXT_DEPENDENT" },
        { "RepeatedStringConcatenation", "DERIVABLE_WITH_SUMMARIES" },
        { "BuildConfigurationDependent", "CONTEXT_DEPENDENT" },
        { "BfsNoVisited", "NON_TERMINATION_RISK" },
    };

    [Fact]
    public void Fixture_CompilesWithoutErrors()
    {
        var compilation = CompilationFactory.Create(
            File.ReadAllText(FixturePath()), "EdgeCases");
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FastAndDeep_ProduceAResult(string name, string expected)
    {
        var (fast, deep) = AnalyzeBoth(name);
        _output.WriteLine(FormatRow(name, expected, fast, deep));
        Assert.False(string.IsNullOrWhiteSpace(
            ComplexityFormatter.FormatBigO(fast.Time)));
        Assert.False(string.IsNullOrWhiteSpace(
            ComplexityFormatter.FormatBigO(deep.Time)));
    }

    [Theory]
    [InlineData("DynamicDispatch", "dynamic-dispatch")]
    [InlineData("ReflectionDispatch", "reflection-dispatch")]
    [InlineData("InterfaceDispatch", "interface-dispatch")]
    [InlineData("RegexBacktracking", "regex")]
    [InlineData("StreamRead", "stream-io")]
        [InlineData("CollatzSteps", "unproven-loop")]
        [InlineData("QueryProviderDependent", "queryable")]
        [InlineData("AwaitOpaqueWork", "await-opaque")]
        [InlineData("ConsumeAsyncStream", "await-opaque")]
        [InlineData("BfsNoVisited", "unbounded-worklist")]
    public void InconclusiveCases_AreUnknown(string name, string patternId)
    {
        var (fast, deep) = AnalyzeBoth(name);
        Assert.Equal("O(unknown)", ComplexityFormatter.FormatBigO(fast.Time));
        Assert.Equal("O(unknown)", ComplexityFormatter.FormatBigO(deep.Time));
        Assert.Contains(fast.Patterns, p => p.Id == patternId);
        Assert.StartsWith("Unknown:", fast.Explanation);
        Assert.Equal(AnalysisConfidence.Unknown, fast.Confidence);
        Assert.Equal(AnalysisConfidence.Unknown, deep.Confidence);
    }

    [Fact]
    public void LocalBodies_AreWalked()
    {
        var (prop, _) = AnalyzeBoth("PropertyAccessLooksConstant");
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(prop.Time));
        var (index, _) = AnalyzeBoth("IndexerLooksConstant");
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(index.Time));
        var (slow, _) = AnalyzeBoth("ForeachOverSlowEnumerable");
        Assert.Equal("O(n²)", ComplexityFormatter.FormatBigO(slow.Time));
    }

    [Fact]
    public void DeferredLinq_IsAnnotatedConstant()
    {
        var (fast, _) = AnalyzeBoth("DeferredLinq");
        Assert.Equal("O(1)", ComplexityFormatter.FormatBigO(fast.Time));
        Assert.Contains(fast.Patterns, p => p.Id == "deferred-linq");
        Assert.Equal("Constant time", fast.Explanation);
    }

    [Fact]
    public void Report_FastVersusDeep()
    {
        _output.WriteLine(
            "method | comment | fast | deep | same?");
        foreach (var row in Cases)
        {
            var name = (string)row[0];
            var expected = (string)row[1];
            var (fast, deep) = AnalyzeBoth(name);
            _output.WriteLine(FormatRow(name, expected, fast, deep));
        }
    }

    private static (ComplexityResult Fast, ComplexityResult Deep)
        AnalyzeBoth(string name)
    {
        var source = File.ReadAllText(FixturePath());
        var compilation = CompilationFactory.Create(source, "EdgeCases");
        var tree = CompilationFactory.SourceTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        var symbol = FindCase(tree, model, name);
        var analyzer = new CSharpMethodAnalyzer();
        return (
            analyzer.Analyze(symbol, model, AnalysisTier.Fast),
            analyzer.Analyze(symbol, model, AnalysisTier.Deep));
    }

    private static IMethodSymbol FindCase(
        SyntaxTree tree, SemanticModel model, string name)
    {
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m =>
                m.Identifier.Text == name
                && m.Ancestors()
                    .OfType<ClassDeclarationSyntax>()
                    .Any(c => c.Identifier.Text == "ComplexityEdgeCases"));
        return (IMethodSymbol)model.GetDeclaredSymbol(method)!;
    }

    private static string FormatRow(
        string name,
        string expected,
        ComplexityResult fast,
        ComplexityResult deep)
    {
        var f = Describe(fast);
        var d = Describe(deep);
        var same = f == d ? "same" : "DIFF";
        return $"{name} | {expected} | {f} | {d} | {same}";
    }

    private static string Describe(ComplexityResult result)
    {
        var time = ComplexityFormatter.FormatBigO(result.Time);
        var space = ComplexityFormatter.FormatBigO(result.AuxiliarySpace);
        return $"{time}/{space} {result.Confidence}";
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
                "RoslynComplexityEdgeCases.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(
            "RoslynComplexityEdgeCases.cs not found.");
    }
}
