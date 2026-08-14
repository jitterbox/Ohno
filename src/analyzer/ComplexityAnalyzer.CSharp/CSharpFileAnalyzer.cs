using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
/// Analyzes every method, constructor, and local function in a C# file.
/// </summary>
public sealed class CSharpFileAnalyzer
{
    private readonly CSharpMethodAnalyzer _methods = new();

    public FileAnalysis Analyze(
        string source,
        AnalysisTier tier,
        CancellationToken token = default)
    {
        var compilation = CompilationFactory.Create(source, "OhnoAdHoc");
        var tree = compilation.SyntaxTrees.Single();
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
            functions.Add(new AnalyzedFunction(method, result, range, signature));
        }

        return new FileAnalysis(functions, Array.Empty<AnalysisWarning>());
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
            LocalFunctionStatementSyntax l => model.GetDeclaredSymbol(l),
            _ => null,
        };
        if (symbol is not IMethodSymbol methodSymbol) return false;
        method = methodSymbol;
        var id = node switch
        {
            MethodDeclarationSyntax m => RoslynSpans.Of(m.Identifier),
            ConstructorDeclarationSyntax c => RoslynSpans.Of(c.Identifier),
            LocalFunctionStatementSyntax l => RoslynSpans.Of(l.Identifier),
            _ => RoslynSpans.Of(node),
        };
        signature = id ?? RoslynSpans.Of(node) ?? signature;
        return true;
    }
}
