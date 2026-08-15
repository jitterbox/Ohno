using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Picks at most three named readings of a function: the dominant
/// bound, nested or sequential work, and an alternative interpretation.
/// </summary>
internal static class ApproachSummarizer
{
    public static (
        IReadOnlyList<AlgorithmApproach> Approaches,
        string Hint)
        Summarize(
            IReadOnlyList<RecognizedPattern> patterns,
            ComplexityEvidence evidence,
            ComplexityExpression time,
            bool selection = false)
    {
        var items = Collect(patterns, evidence, time)
            .DistinctBy(a => a.Id)
            .Take(3)
            .ToArray();
        var hint = items.Length > 1
            ? selection
                ? "This selection still combines more than one approach. "
                  + "Narrow the selection for a tighter "
                  + "per-algorithm bound."
                : "This function combines more than one approach. "
                  + "Select a smaller region for a tighter "
                  + "per-algorithm bound."
            : "";
        return (items, hint);
    }

    private static IEnumerable<AlgorithmApproach> Collect(
        IReadOnlyList<RecognizedPattern> patterns,
        ComplexityEvidence evidence,
        ComplexityExpression time)
    {
        foreach (var pattern in patterns)
        {
            foreach (var item in FromPattern(pattern, time))
                yield return item;
        }

        foreach (var item in FromEvidence(evidence, patterns))
            yield return item;
    }

    private static IEnumerable<AlgorithmApproach> FromPattern(
        RecognizedPattern pattern, ComplexityExpression time)
    {
        var gloss = ComplexityFormatter.FormatBigO(time);
        return pattern.Id switch
        {
            "deferred-linq" => LinqApproaches(),
            "queryable" => QueryableApproaches(),
            "cache-history" => CacheApproaches(gloss),
            "data-dependent-recursion" => BranchApproaches(),
            "bounded-recursion" => BoundApproaches(pattern),
            _ when IsAlgorithm(pattern.Id) =>
                One(pattern, "dominant", gloss),
            _ => One(pattern, RoleOf(pattern), ""),
        };
    }

    private static IEnumerable<AlgorithmApproach> LinqApproaches()
    {
        yield return new AlgorithmApproach(
            "deferred-linq",
            "Deferred LINQ construction",
            "Enumerable operators build a query in constant time. "
            + "This is in-memory LINQ, not EF.",
            "dominant",
            "O(1)");
        yield return new AlgorithmApproach(
            "deferred-linq-enum",
            "Full enumeration",
            "If the caller enumerates the whole sequence, pay the "
            + "operator costs (typically linear in the source).",
            "alternative",
            "O(n)");
    }

    private static IEnumerable<AlgorithmApproach> QueryableApproaches()
    {
        yield return new AlgorithmApproach(
            "queryable",
            "IQueryable / EF provider",
            "The expression tree is executed by a provider. "
            + "SQL shape and row counts are not in this method.",
            "dominant",
            "O(unknown)");
        yield return new AlgorithmApproach(
            "queryable-scan",
            "If the provider scans rows",
            "A simple EF scan is often linear in rows examined. "
            + "That is not proven from this source.",
            "alternative");
    }

    private static IEnumerable<AlgorithmApproach> CacheApproaches(
        string gloss)
    {
        yield return new AlgorithmApproach(
            "cache-miss",
            "Cache miss / uncached work",
            "A miss repeats the full computation.",
            "dominant",
            gloss);
        yield return new AlgorithmApproach(
            "cache-hit",
            "Cache hit",
            "A hit is a dictionary lookup.",
            "alternative",
            "O(1)");
    }

    private static IEnumerable<AlgorithmApproach> BranchApproaches()
    {
        yield return new AlgorithmApproach(
            "data-dependent-recursion",
            "Data-dependent recursion",
            "The number of recursive calls depends on input values.",
            "dominant");
        yield return new AlgorithmApproach(
            "single-branch",
            "Single-branch path",
            "If only one recursive call is taken, work is linear "
            + "in remaining elements.",
            "alternative",
            "O(n)");
        yield return new AlgorithmApproach(
            "both-branches",
            "Both branches taken",
            "If both recursive calls are taken at every step, "
            + "work is exponential.",
            "alternative",
            "O(2^n)");
    }

    private static IEnumerable<AlgorithmApproach> BoundApproaches(
        RecognizedPattern pattern)
    {
        yield return new AlgorithmApproach(
            pattern.Id,
            pattern.Label,
            pattern.Reason,
            "alternative",
            "O(2^k)");
    }

    private static IEnumerable<AlgorithmApproach> One(
        RecognizedPattern pattern, string role, string hint)
    {
        yield return new AlgorithmApproach(
            pattern.Id,
            pattern.Label,
            pattern.Reason,
            role,
            hint);
    }

    private static IEnumerable<AlgorithmApproach> FromEvidence(
        ComplexityEvidence evidence,
        IReadOnlyList<RecognizedPattern> patterns)
    {
        if (patterns.Any(p => IsAlgorithm(p.Id))) yield break;
        var kids = Significant(evidence);
        if (kids.Count < 2) yield break;
        for (var i = 0; i < kids.Count && i < 3; i++)
        {
            var child = kids[i];
            yield return new AlgorithmApproach(
                "seq:" + child.Kind + ":" + i,
                child.Label,
                "This step contributes "
                + ComplexityFormatter.Format(child.Cost) + ".",
                "sequential",
                ComplexityFormatter.FormatBigO(child.Cost));
        }
    }

    private static List<ComplexityEvidence> Significant(
        ComplexityEvidence evidence)
    {
        var root = evidence.Kind == "sequence" && evidence.Children.Count > 0
            ? evidence.Children
            : new[] { evidence };
        return root.Where(c =>
                c.Kind is "loop" or "linq" or "recursion" or "call")
            .ToList();
    }

    private static bool IsAlgorithm(string id) =>
        id is "binary-search"
            or "memoized-recursion"
            or "subset-generation"
            or "combinatorial-generation"
            or "graph-traversal"
            or "branching-recursion"
            or "linear-recurrence"
            or "divide-and-conquer";

    private static string RoleOf(RecognizedPattern pattern) =>
        pattern.Effect == PatternEffect.Unknown ? "dominant"
        : pattern.Id is "await-opaque" or "queryable" or "stream-io"
            or "lock-wait" or "iterator-yield"
            ? "nested"
            : "dominant";
}
