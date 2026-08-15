using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.CSharp;

public sealed record AnalyzedFunction(
    IMethodSymbol Symbol,
    ComplexityResult Result,
    LineSpan Range,
    LineSpan SignatureRange,
    bool IsSelection = false);

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
            var result = _methods.Analyze(method, model, tier, token);
            var range = RoslynSpans.Of(node) ?? signature;
            functions.Add(
                new AnalyzedFunction(method, result, range, signature));
        }

        TryAddEntryPoint(model, tier, functions, token);
        return new FileAnalysis(functions, BindWarnings.For(model));
    }

    public FileAnalysis AnalyzeSelection(
        string source,
        LineSpan span,
        AnalysisTier tier,
        CancellationToken token = default)
    {
        var compilation = CompilationFactory.Create(source, "OhnoAdHoc");
        var tree = CompilationFactory.SourceTree(compilation);
        var model = compilation.GetSemanticModel(tree);
        return AnalyzeSelection(model, tree, span, tier, token);
    }

    public FileAnalysis AnalyzeSelection(
        SemanticModel model,
        SyntaxTree tree,
        LineSpan span,
        AnalysisTier tier,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var method = SelectionFragment.EnclosingMethod(model, tree, span);
        if (method is null)
        {
            var warning = new AnalysisWarning(
                "Select a statement or loop inside a method.", span);
            return new FileAnalysis([], [warning]);
        }

        var result = _methods.AnalyzeSelection(
            method, model, span, tier, token);
        var range = result.Evidence.Span ?? span;
        // File-level bind warnings are a property of the document, not
        // of the span, and the document pass already produced them.
        // Recomputing here would force a full bind of every method body
        // in the file just to score a two-line selection.
        return new FileAnalysis(
            [new AnalyzedFunction(method, result, range, span, true)],
            []);
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
        var globals = unit.Members
            .OfType<GlobalStatementSyntax>()
            .ToArray();
        if (globals.Length == 0) return;
        var signature = RoslynSpans.Of(globals[0])
            ?? new LineSpan(0, 0, 0, 0);
        var range = SpanOfGlobals(globals, signature);
        var result = _methods.Analyze(main, model, tier, token);
        functions.Insert(
            0, new AnalyzedFunction(main, result, range, signature));
    }

    private static LineSpan SpanOfGlobals(
        GlobalStatementSyntax[] globals, LineSpan fallback)
    {
        var first = RoslynSpans.Of(globals[0]) ?? fallback;
        var last = RoslynSpans.Of(globals[^1]) ?? first;
        return new LineSpan(
            first.StartLine,
            first.StartCharacter,
            last.EndLine,
            last.EndCharacter);
    }
}
