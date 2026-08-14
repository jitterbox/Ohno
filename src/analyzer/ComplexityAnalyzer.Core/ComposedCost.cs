namespace ComplexityAnalyzer.Core;

/// <summary>
/// Intermediate cost of a statement or subtree. Sequential composition
/// adds time and takes peak space; conditionals take the worst branch.
/// </summary>
public sealed record ComposedCost
{
    public required ComplexityExpression Time { get; init; }

    public required ComplexityExpression Space { get; init; }

    public AnalysisConfidence Confidence { get; init; } =
        AnalysisConfidence.High;

    public required ComplexityEvidence Evidence { get; init; }

    public IReadOnlyList<AnalysisWarning> Warnings { get; init; } = [];

    public IReadOnlyList<BoundingSuggestion> Suggestions { get; init; } = [];

    public static ComposedCost Unit(string kind, string label, LineSpan? span)
    {
        return new ComposedCost
        {
            Time = Cx.One,
            Space = Cx.One,
            Evidence = ComplexityEvidence.Leaf(kind, label, Cx.One, span),
        };
    }

    public static ComposedCost Of(
        ComplexityExpression time,
        ComplexityExpression space,
        string kind,
        string label,
        LineSpan? span,
        AnalysisConfidence confidence = AnalysisConfidence.High)
    {
        return new ComposedCost
        {
            Time = time,
            Space = space,
            Confidence = confidence,
            Evidence = ComplexityEvidence.Leaf(kind, label, time, span),
        };
    }

    public static AnalysisConfidence Min(
        AnalysisConfidence a, AnalysisConfidence b) =>
        a < b ? a : b;
}
