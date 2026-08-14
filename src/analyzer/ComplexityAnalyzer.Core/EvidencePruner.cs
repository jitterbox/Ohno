namespace ComplexityAnalyzer.Core;

/// <summary>
/// Drops O(1) placeholders that do not affect the reported bound.
/// Empty statement lists, locals, and literals become <c>empty: 1</c>
/// during composition; they are absorbed algebraically and should not
/// appear in the derivation tree.
/// </summary>
public static class EvidencePruner
{
    public static ComplexityEvidence Prune(ComplexityEvidence evidence)
    {
        var children = evidence.Children
            .Select(Prune)
            .Where(c => !IsNoise(c))
            .ToArray();

        if (evidence.Kind == "sequence" && children.Length == 1)
            return children[0];

        return evidence with { Children = children };
    }

    public static ComplexityEvidence Sequence(
        ComplexityExpression time,
        LineSpan? span,
        IEnumerable<ComplexityEvidence> parts)
    {
        var children = parts
            .Select(Prune)
            .Where(c => !IsNoise(c))
            .ToArray();
        if (children.Length == 1)
            return children[0];
        if (children.Length == 0)
        {
            return ComplexityEvidence.Leaf(
                "sequence", "empty", time, span);
        }

        return new ComplexityEvidence(
            "sequence",
            "sequential statements",
            time,
            span,
            children);
    }

    public static IReadOnlyList<ComplexityEvidence> Meaningful(
        IEnumerable<ComplexityEvidence> parts) =>
        parts.Select(Prune).Where(c => !IsNoise(c)).ToArray();

    public static bool IsNoise(ComplexityEvidence evidence)
    {
        if (evidence.Label == "empty") return true;
        return evidence.Kind == "sequence"
            && evidence.Children.Count == 0
            && evidence.Cost is ConstantExpression { Value: 1 };
    }
}
