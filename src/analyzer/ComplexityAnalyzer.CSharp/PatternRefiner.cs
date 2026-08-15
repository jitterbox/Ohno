using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Merges recurrence classifications into the pattern list and
/// downgrades incidental opacity when a structural bound exists.
/// </summary>
internal static class PatternRefiner
{
    public static IReadOnlyList<RecognizedPattern> Refine(
        IReadOnlyList<RecognizedPattern> patterns,
        ComplexityExpression time,
        AnalysisState state)
    {
        var merged = MergeRecurrence(patterns, state);
        var structural = HasStructural(time);
        var solved = state.RecurrenceId is not null;
        return merged
            .Where(p => Keep(p, solved))
            .Select(p => Soften(p, structural))
            .ToArray();
    }

    private static IReadOnlyList<RecognizedPattern> MergeRecurrence(
        IReadOnlyList<RecognizedPattern> patterns,
        AnalysisState state)
    {
        var extra = new List<RecognizedPattern>();
        if (state.RecurrenceId is { } id)
        {
            extra.Add(new RecognizedPattern(
                id,
                Title(id, state.RecurrenceLabel),
                "closed form from the recursive call shape, "
                + "not a named textbook proof",
                PatternEffect.Annotate));
        }

        if (state.RecurrenceBound is { } bound)
        {
            extra.Add(new RecognizedPattern(
                "bounded-recursion",
                "Bounded recursion",
                "parameter " + bound + " may cap the recursion tree; "
                + "the headline uses the unbounded shape",
                PatternEffect.Annotate));
        }

        return extra.Concat(patterns).DistinctBy(p => p.Id).ToArray();
    }

    private static bool Keep(RecognizedPattern pattern, bool solved) =>
        !solved || pattern.Id != "data-dependent-recursion";

    private static RecognizedPattern Soften(
        RecognizedPattern pattern, bool structural)
    {
        if (!structural || !IsSoft(pattern.Id)) return pattern;
        if (pattern.Effect != PatternEffect.Unknown) return pattern;
        return pattern with
        {
            Effect = PatternEffect.Annotate,
            Reason = pattern.Reason
                + "; the local loop or recurrence bound is kept",
        };
    }

    private static bool IsSoft(string id) =>
        id is "await-opaque"
            or "stream-io"
            or "queryable"
            or "thread-block"
            or "interface-dispatch"
            or "delegate-invoke";

    private static bool HasStructural(ComplexityExpression time) =>
        time is VariableExpression
            or LogExpression
            or PowerExpression
            or ProductExpression
            or SumExpression
            or FactorialExpression
            or BinomialExpression;

    private static string Title(string id, string? label) =>
        id switch
        {
            "binary-search" => "Binary search",
            "memoized-recursion" => "Memoized recursion",
            "subset-generation" => "Subset generation",
            "combinatorial-generation" =>
                "Permutation / combination generation",
            "graph-traversal" => "Visited graph walk",
            "branching-recursion" => "Branching recursion",
            "linear-recurrence" => "Linear recursion",
            "divide-and-conquer" => "Divide and conquer",
            _ => label ?? id,
        };
}
