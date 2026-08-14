using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.CSharp;

internal static class PatternApplicator
{
    public static ComplexityExpression ApplyTime(
        ComplexityExpression time,
        IReadOnlyList<RecognizedPattern> patterns)
    {
        var opaque = patterns.FirstOrDefault(p => IsOpaque(p.Id));
        if (opaque is not null)
            return Cx.Unknown(opaque.Reason);

        var unknown = patterns.FirstOrDefault(
            p => p.Effect == PatternEffect.Unknown);
        if (unknown is not null
            && time is ConstantExpression or FunctionCostExpression)
        {
            return Cx.Unknown(unknown.Reason);
        }

        var range = patterns.FirstOrDefault(
            p => p.Effect == PatternEffect.Range);
        if (range is not null && ContainsCall(time))
            return Cx.Unknown(range.Reason);

        if (patterns.Any(p => p.Id == "string-concat-loop")
            && time is VariableExpression variable)
        {
            return Cx.Mul(variable, variable);
        }

        return time;
    }

    public static ComplexityExpression ApplySpace(
        ComplexityExpression space,
        IReadOnlyList<RecognizedPattern> patterns)
    {
        if (patterns.Any(p => p.Id == "unbounded-worklist"))
            return Cx.Unknown("worklist");
        return space;
    }

    private static bool IsOpaque(string id) =>
        id is "dynamic-dispatch"
            or "reflection-dispatch"
            or "regex"
            or "stream-io"
            or "queryable"
            or "expression-compile"
            or "parallel-loop"
            or "await-opaque"
            or "unproven-loop"
            or "thread-block"
            or "unbounded-worklist";

    private static bool ContainsCall(ComplexityExpression expression) =>
        expression is FunctionCostExpression
        || expression is ProductExpression p
            && p.Factors.Any(ContainsCall)
        || expression is SumExpression s
            && s.Terms.Any(ContainsCall);
}
