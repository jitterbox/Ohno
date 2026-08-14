namespace ComplexityAnalyzer.Core;

/// <summary>
/// Language-neutral composition of sequential, branch, and loop costs.
/// </summary>
/// <remarks>
/// Space is peak simultaneously retained memory, not allocation volume.
/// A loop that allocates and drops a buffer each iteration stays Θ(size),
/// not Θ(iterations × size). Time still multiplies by the bound.
/// Mutually exclusive branches take the worst case; they are not added.
/// </remarks>
public static class CostComposer
{
    public static ComposedCost Sequential(
        IReadOnlyList<ComposedCost> parts, LineSpan? span)
    {
        if (parts.Count == 0)
            return ComposedCost.Unit("sequence", "empty", span);
        if (parts.Count == 1) return parts[0];

        var time = ComplexitySimplifier.Simplify(
            Cx.Add(parts.Select(p => p.Time)));
        var space = ComplexitySimplifier.Simplify(
            Peak(parts.Select(p => p.Space)));
        var confidence = parts.Min(p => p.Confidence);
        var evidence = EvidencePruner.Sequence(
            time, span, parts.Select(p => p.Evidence));

        return new ComposedCost
        {
            Time = time,
            Space = space,
            Confidence = confidence,
            Evidence = evidence,
            Warnings = Concat(parts, p => p.Warnings),
            Suggestions = Concat(parts, p => p.Suggestions),
        };
    }

    public static ComposedCost Conditional(
        ComposedCost condition,
        ComposedCost whenTrue,
        ComposedCost? whenFalse,
        LineSpan? span)
    {
        var branch = whenFalse is null
            ? whenTrue.Time
            : Cx.Add(Cx.One); // placeholder replaced below
        if (whenFalse is not null)
        {
            // Worst case: do not add mutually exclusive branches.
            branch = MaxExpr(whenTrue.Time, whenFalse.Time);
        }
        else
        {
            branch = whenTrue.Time;
        }

        var time = ComplexitySimplifier.Simplify(
            Cx.Add(condition.Time, branch));
        var spaceParts = whenFalse is null
            ? new[] { condition.Space, whenTrue.Space }
            : new[] { condition.Space, whenTrue.Space, whenFalse.Space };
        var space = ComplexitySimplifier.Simplify(Peak(spaceParts));
        var confidence = ComposedCost.Min(
            condition.Confidence,
            ComposedCost.Min(
                whenTrue.Confidence,
                whenFalse?.Confidence ?? AnalysisConfidence.High));

        var children = new List<ComplexityEvidence>
        {
            condition.Evidence,
            whenTrue.Evidence,
        };
        if (whenFalse is not null) children.Add(whenFalse.Evidence);

        return new ComposedCost
        {
            Time = time,
            Space = space,
            Confidence = confidence,
            Evidence = new ComplexityEvidence(
                "conditional",
                "worst-case branch",
                time,
                span,
                EvidencePruner.Meaningful(children)),
            Warnings = Concat(
                new[] { condition, whenTrue, whenFalse }
                    .OfType<ComposedCost>(),
                p => p.Warnings)
                .Append(new AnalysisWarning(
                    "Worst-case analysis used for branches."))
                .ToArray(),
            Suggestions = Concat(
                new[] { condition, whenTrue, whenFalse }
                    .OfType<ComposedCost>(),
                p => p.Suggestions),
        };
    }

    public static ComposedCost Loop(
        ComplexityExpression bound,
        ComposedCost body,
        string label,
        LineSpan? span)
    {
        var time = ComplexitySimplifier.Simplify(Cx.Mul(bound, body.Time));
        var space = ComplexitySimplifier.Simplify(body.Space);
        var evidence = new ComplexityEvidence(
            "loop",
            label,
            time,
            span,
            new[] { body.Evidence });

        return new ComposedCost
        {
            Time = time,
            Space = space,
            Confidence = body.Confidence,
            Evidence = evidence,
            Warnings = body.Warnings,
            Suggestions = body.Suggestions,
        };
    }

    public static ComplexityExpression Peak(
        IEnumerable<ComplexityExpression> spaces)
    {
        var list = spaces.ToList();
        if (list.Count == 0) return Cx.One;
        if (list.Count == 1) return list[0];
        // Peak auxiliary space is the max, which Big-O dominance captures.
        return ComplexitySimplifier.Simplify(Cx.Add(list));
    }

    public static ComplexityExpression MaxExpr(
        ComplexityExpression a, ComplexityExpression b)
    {
        var simplified = ComplexitySimplifier.Simplify(Cx.Add(a, b));
        return simplified;
    }

    private static IReadOnlyList<T> Concat<T>(
        IEnumerable<ComposedCost> parts,
        Func<ComposedCost, IReadOnlyList<T>> selector) =>
        parts.SelectMany(selector).ToArray();
}
