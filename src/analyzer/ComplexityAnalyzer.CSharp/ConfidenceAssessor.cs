using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Caps confidence for idiom matches and collects human-readable
/// reasons whenever the result is below High.
/// </summary>
/// <remarks>
/// Reasons are shown in the Complexity panel under the confidence
/// row. They are not a proof of unsoundness; they name the assumption
/// that would fail if the source used a different loop, type, or
/// store shape.
/// </remarks>
internal static class ConfidenceAssessor
{
    public static (AnalysisConfidence Confidence, string[] Reasons)
        Assess(
            AnalysisConfidence current,
            IReadOnlyList<RecognizedPattern> patterns,
            AnalysisState state,
            ComplexityExpression time)
    {
        var notes = new List<(AnalysisConfidence Cap, string Reason)>();
        notes.AddRange(state.Notes);
        AddPatternNotes(patterns, notes);
        AddExpressionNotes(time, notes);

        var confidence = current;
        foreach (var (cap, _) in notes)
        {
            if (cap < confidence) confidence = cap;
        }

        if (confidence >= AnalysisConfidence.High)
            return (AnalysisConfidence.High, []);

        var reasons = notes
            .Select(n => n.Reason)
            .Distinct()
            .ToArray();
        return (confidence, reasons);
    }

    private static void AddPatternNotes(
        IReadOnlyList<RecognizedPattern> patterns,
        List<(AnalysisConfidence, string)> notes)
    {
        foreach (var pattern in patterns)
        {
            var cap = pattern.Effect switch
            {
                PatternEffect.Unknown => AnalysisConfidence.Unknown,
                PatternEffect.Range => AnalysisConfidence.Medium,
                _ when IsAssumption(pattern.Id) =>
                    AnalysisConfidence.Medium,
                _ => AnalysisConfidence.High,
            };
            if (cap >= AnalysisConfidence.High) continue;
            notes.Add((cap, pattern.Reason));
        }
    }

    private static void AddExpressionNotes(
        ComplexityExpression time,
        List<(AnalysisConfidence, string)> notes)
    {
        if (ContainsUnknown(time))
        {
            notes.Add((
                AnalysisConfidence.Unknown,
                "The bound contains a cost that could not be resolved."));
        }

        if (ContainsCall(time))
        {
            notes.Add((
                AnalysisConfidence.Low,
                "One or more calls were left as C(name) because "
                + "the target body is not fixed here."));
        }
    }

    private static bool IsAssumption(string id) =>
        id is "null-terminated-walk"
            or "numeric-countdown"
            or "lock-wait"
            or "iterator-yield"
            or "string-concat-loop"
            or "await-opaque"
            or "stream-io"
            or "queryable"
            or "thread-block"
            or "interface-dispatch"
            or "delegate-invoke"
            or "bounded-recursion"
            or "binary-search"
            or "memoized-recursion"
            or "subset-generation"
            or "combinatorial-generation"
            or "graph-traversal"
            or "branching-recursion"
            or "linear-recurrence"
            or "divide-and-conquer";

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
}
