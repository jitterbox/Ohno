using System.Collections.Immutable;
using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ComplexityAnalyzer.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComplexityDiagnosticAnalyzer : DiagnosticAnalyzer
{
    public const string EstimateId = "AL0001";
    public const string PartialId = "AL0002";
    public const string UnavailableId = "AL0003";

    private static readonly DiagnosticDescriptor Estimate = new(
        EstimateId,
        "Complexity estimate",
        "{0}",
        "Complexity",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Partial = new(
        PartialId,
        "Complexity partially unresolved",
        "{0}",
        "Complexity",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Unavailable = new(
        UnavailableId,
        "Complexity analysis unavailable",
        "{0}",
        "Complexity",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Estimate, Partial, Unavailable);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzeMethod, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzeMethod, SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.SemanticModel.GetDeclaredSymbol(
            context.Node, context.CancellationToken);
        if (symbol is not IMethodSymbol method) return;

        var analyzer = new CSharpMethodAnalyzer();
        var result = analyzer.Analyze(
            method, context.SemanticModel, AnalysisTier.Fast);
        var message = ComplexityFormatter.FormatHeadline(result);
        var descriptor = result.Confidence switch
        {
            AnalysisConfidence.Unknown => Unavailable,
            AnalysisConfidence.Low => Partial,
            _ => Estimate,
        };
        var location = Identifier(context.Node);
        context.ReportDiagnostic(
            Diagnostic.Create(descriptor, location, message));
    }

    private static Location Identifier(SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax m => m.Identifier.GetLocation(),
            ConstructorDeclarationSyntax c => c.Identifier.GetLocation(),
            LocalFunctionStatementSyntax l => l.Identifier.GetLocation(),
            _ => node.GetLocation(),
        };
}
