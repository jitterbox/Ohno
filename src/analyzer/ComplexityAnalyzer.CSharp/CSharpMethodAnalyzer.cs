using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Walks a method as an <see cref="IOperation"/> tree and composes
/// symbolic time and peak auxiliary space.
/// </summary>
/// <remarks>
/// Bodies come from <see cref="SemanticModel.GetOperation(SyntaxNode)"/>,
/// which yields a language-agnostic semantic graph rather than raw syntax.
/// See
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.ioperation">IOperation</see>
/// and
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.semanticmodel.getoperation">GetOperation</see>.
/// Local-function declarations are not executed at the declaration site;
/// cost is paid at each call. Recursive methods are classified by
/// <see cref="RecurrenceAnalyzer"/> instead of unrolling the tree.
/// </remarks>
public sealed partial class CSharpMethodAnalyzer
{
    private readonly OperationCatalog _catalog;

    public CSharpMethodAnalyzer(OperationCatalog? catalog = null)
    {
        _catalog = catalog ?? OperationCatalog.CreateDefault();
    }

    public ComplexityResult Analyze(
        IMethodSymbol method,
        SemanticModel model,
        AnalysisTier tier)
    {
        var state = new AnalysisState(tier) { Catalog = _catalog };
        DimensionInferrer.Infer(method, state);
        var cost = AnalyzeSymbol(method, model, state);
        var time = ComplexitySimplifier.Simplify(cost.Time);
        var space = ComplexitySimplifier.Simplify(
            CostComposer.Peak(state.Retained.Append(cost.Space)));
        var confidence = Downgrade(cost, time);
        var evidence = EvidencePruner.Prune(cost.Evidence);
        var body = TryGetBody(method, model);
        var patterns = PatternRecognizer.Recognize(method, body);
        time = PatternApplicator.ApplyTime(time, patterns);
        space = PatternApplicator.ApplySpace(space, patterns);
        var assessed = ConfidenceAssessor.Assess(
            confidence, patterns, state, time);
        confidence = assessed.Confidence;
        var explanation = ExplanationFormatter.Format(time, patterns);
        var warnings = MergeWarnings(cost, state, patterns);
        var suggestions = cost.Suggestions
            .Concat(state.Suggestions)
            .ToArray();

        return new ComplexityResult(
            time,
            space,
            confidence,
            state.Dimensions,
            evidence,
            warnings,
            suggestions,
            patterns,
            explanation,
            assessed.Reasons);
    }

    internal ComposedCost AnalyzeSymbol(
        IMethodSymbol method,
        SemanticModel model,
        AnalysisState state)
    {
        if (state.Cache.TryGetValue(method, out var cached))
            return cached;
        if (!state.Analyzing.Add(method))
            return RecurrencePlaceholder(method);
        if (state.Depth >= AnalysisState.MaxDepth)
        {
            state.Analyzing.Remove(method);
            return UnknownCall(method.Name, null, "analysis depth exceeded");
        }

        state.Depth++;
        var cost = AnalyzeBody(method, model, state);
        state.Depth--;
        state.Analyzing.Remove(method);
        state.Cache[method] = cost;
        return cost;
    }

    private ComposedCost AnalyzeBody(
        IMethodSymbol method,
        SemanticModel model,
        AnalysisState state)
    {
        var syntax = method.DeclaringSyntaxReferences
            .FirstOrDefault()
            ?.GetSyntax();
        if (syntax is null)
            return UnknownCall(method.Name, null, "no syntax");

        var body = TryGetBody(method, model);
        if (body is null)
            return ComposedCost.Unit("method", method.Name, RoslynSpans.Of(syntax));

        var rec = RecurrenceAnalyzer.TrySolve(method, body, state);
        if (rec is not null) return rec;

        CardinalityAnalyzer.Analyze(body, model, state);
        return Analyze(body, state);
    }

    private static IOperation? TryGetBody(
        IMethodSymbol method, SemanticModel model)
    {
        var syntax = method.DeclaringSyntaxReferences
            .FirstOrDefault()
            ?.GetSyntax();
        if (syntax is null) return null;
        return ExtractBody(model.GetOperation(syntax), syntax, model);
    }

    private static IReadOnlyList<AnalysisWarning> MergeWarnings(
        ComposedCost cost,
        AnalysisState state,
        IReadOnlyList<RecognizedPattern> patterns)
    {
        var fromPatterns = patterns
            .Where(p => p.Effect == PatternEffect.Unknown)
            .Select(p => new AnalysisWarning($"{p.Label}: {p.Reason}."));
        return cost.Warnings
            .Concat(state.Warnings)
            .Concat(fromPatterns)
            .DistinctBy(w => w.Message)
            .ToArray();
    }

    private static IOperation? ExtractBody(
        IOperation? operation,
        SyntaxNode syntax,
        SemanticModel model)
    {
        return operation switch
        {
            IMethodBodyOperation body =>
                body.BlockBody ?? body.ExpressionBody,
            IConstructorBodyOperation body =>
                body.BlockBody ?? body.ExpressionBody,
            ILocalFunctionOperation local =>
                local.Body ?? local.ChildOperations.FirstOrDefault(),
            _ when syntax is ArrowExpressionClauseSyntax arrow =>
                model.GetOperation(arrow),
            _ => operation,
        };
    }

    private static AnalysisConfidence Downgrade(
        ComposedCost cost, ComplexityExpression time)
    {
        if (ContainsUnknown(time)) return AnalysisConfidence.Unknown;
        if (ContainsCall(time)) return AnalysisConfidence.Low;
        return cost.Confidence;
    }

    private static bool ContainsUnknown(ComplexityExpression expression) =>
        expression is UnknownExpression
        || ChildrenOf(expression).Any(ContainsUnknown);

    private static bool ContainsCall(ComplexityExpression expression) =>
        expression is FunctionCostExpression
        || ChildrenOf(expression).Any(ContainsCall);

    private static IEnumerable<ComplexityExpression> ChildrenOf(
        ComplexityExpression expression) =>
        expression switch
        {
            SumExpression s => s.Terms,
            ProductExpression p => p.Factors,
            LogExpression l => new[] { l.Inner },
            PowerExpression p => new[] { p.Base, p.Exponent },
            FactorialExpression f => new[] { f.Inner },
            BinomialExpression b => new[] { b.N, b.K },
            _ => Array.Empty<ComplexityExpression>(),
        };

    private static ComposedCost RecurrencePlaceholder(IMethodSymbol method)
    {
        return ComposedCost.Of(
            Cx.Call(method.Name),
            Cx.Var("n"),
            "recursion",
            $"recursive {method.Name}",
            null,
            AnalysisConfidence.Low);
    }

    private static ComposedCost UnknownCall(
        string name, LineSpan? span, string reason)
    {
        return new ComposedCost
        {
            Time = Cx.Call(name),
            Space = Cx.One,
            Confidence = AnalysisConfidence.Low,
            Evidence = ComplexityEvidence.Leaf("call", name, Cx.Call(name), span),
            Warnings = new[] { new AnalysisWarning(reason, span) },
        };
    }
}
