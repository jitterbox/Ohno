using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.CSharp;

public sealed record AnalyzedFunction(
    IMethodSymbol Symbol,
    ComplexityResult Result,
    LineSpan Range,
    LineSpan SignatureRange);

public sealed record FileAnalysis(
    IReadOnlyList<AnalyzedFunction> Functions,
    IReadOnlyList<AnalysisWarning> Warnings);

/// <summary>
/// Analyzes every method and constructor in a C# file, plus the
/// synthesized entry point for top-level statements.
/// </summary>
/// <remarks>
/// Fast tier: project <see cref="SemanticModel"/> when the workspace
/// is ready, otherwise <see cref="CompilationFactory"/>.
/// Deep tier: the same walk on a model from
/// <c>MSBuildWorkspace</c> when a solution is loaded.
/// Local functions are paid at the call site and are not listed as
/// top-level results.
/// </remarks>
public sealed class CSharpFileAnalyzer
{
    private readonly CSharpMethodAnalyzer _methods = new();

    public FileAnalysis Analyze(
        string source,
        AnalysisTier tier,
        CancellationToken token = default)
    {
        var compilation = CompilationFactory.Create(source, "OhnoAdHoc");
        var tree = CompilationFactory.SourceTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        return Analyze(model, tree, tier, token);
    }

    public FileAnalysis Analyze(
        SemanticModel model,
        SyntaxTree tree,
        AnalysisTier tier,
        CancellationToken token)
    {
        var root = tree.GetRoot(token);
        var functions = new List<AnalyzedFunction>();
        foreach (var node in root.DescendantNodes())
        {
            token.ThrowIfCancellationRequested();
            if (!TryGetMethod(node, model, out var method, out var signature))
                continue;
            var result = _methods.Analyze(method, model, tier);
            var range = RoslynSpans.Of(node) ?? signature;
            functions.Add(
                new AnalyzedFunction(method, result, range, signature));
        }

        TryAddEntryPoint(model, tier, functions, token);
        return new FileAnalysis(functions, BindWarnings.For(model));
    }

    private static bool TryGetMethod(
        SyntaxNode node,
        SemanticModel model,
        out IMethodSymbol method,
        out LineSpan signature)
    {
        method = null!;
        signature = new LineSpan(0, 0, 0, 0);
        var symbol = node switch
        {
            MethodDeclarationSyntax m => model.GetDeclaredSymbol(m),
            ConstructorDeclarationSyntax c => model.GetDeclaredSymbol(c),
            _ => null,
        };
        if (symbol is not IMethodSymbol methodSymbol) return false;
        method = methodSymbol;
        var id = node switch
        {
            MethodDeclarationSyntax m => RoslynSpans.Of(m.Identifier),
            ConstructorDeclarationSyntax c => RoslynSpans.Of(c.Identifier),
            _ => RoslynSpans.Of(node),
        };
        signature = id ?? RoslynSpans.Of(node) ?? signature;
        return true;
    }

    private void TryAddEntryPoint(
        SemanticModel model,
        AnalysisTier tier,
        List<AnalyzedFunction> functions,
        CancellationToken token)
    {
        var main = model.Compilation.GetEntryPoint(token);
        if (main is null) return;
        if (functions.Any(f =>
            SymbolEqualityComparer.Default.Equals(f.Symbol, main)))
        {
            return;
        }

        var root = model.SyntaxTree.GetRoot(token);
        if (root is not CompilationUnitSyntax unit) return;
        var first = unit.Members
            .OfType<GlobalStatementSyntax>()
            .FirstOrDefault();
        if (first is null) return;
        var span = RoslynSpans.Of(first)
            ?? new LineSpan(0, 0, 0, 0);
        var result = _methods.Analyze(main, model, tier);
        functions.Insert(
            0, new AnalyzedFunction(main, result, span, span));
    }
}
