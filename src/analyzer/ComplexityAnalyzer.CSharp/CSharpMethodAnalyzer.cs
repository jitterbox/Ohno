using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

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
        var state = new AnalysisState(tier);
        DimensionInferrer.Infer(method, state);
        var cost = AnalyzeSymbol(method, model, state);
        var time = ComplexitySimplifier.Simplify(cost.Time);
        var space = ComplexitySimplifier.Simplify(
            CostComposer.Peak(state.Retained.Append(cost.Space)));
        var confidence = Downgrade(cost, time);
        var evidence = EvidencePruner.Prune(cost.Evidence);
        var warnings = cost.Warnings
            .Concat(state.Warnings)
            .DistinctBy(w => w.Message)
            .ToArray();
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
            suggestions);
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

        var operation = model.GetOperation(syntax);
        var body = ExtractBody(operation, syntax, model);
        if (body is null)
            return ComposedCost.Unit("method", method.Name, RoslynSpans.Of(syntax));

        var rec = RecurrenceAnalyzer.TrySolve(method, body, state);
        if (rec is not null) return rec;

        return Analyze(body, state);
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
