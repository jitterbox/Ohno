namespace ComplexityAnalyzer.Core;

/// <summary>
/// Short plain-language gloss for a result. Empty when no honest phrase
/// exists. Unknown results use a fixed "because" sentence.
/// </summary>
/// <remarks>
/// Phrases describe the simplified time bound (Constant, Linear,
/// Quadratic, Linearithmic, Exponential, Factorial, Combinatorial).
/// Space-specific idioms are named on <c>RecognizedPattern</c> and in
/// <c>ConfidenceReasons</c>, not here. <c>O(n C(f))</c> has no honest
/// single phrase and stays empty.
/// </remarks>
public static class ExplanationFormatter
{
    public static string Format(
        ComplexityExpression time,
        IReadOnlyList<RecognizedPattern> patterns)
    {
        if (time is UnknownExpression unknownTime)
        {
            var reason = patterns.FirstOrDefault(
                    p => p.Effect == PatternEffect.Unknown)?.Reason
                ?? unknownTime.Reason
                ?? "it depends on information that is not in this method";
            return UnknownText(reason);
        }

        var range = patterns.FirstOrDefault(
            p => p.Effect == PatternEffect.Range
                && p.RangeExplanation.Length > 0);
        if (range is not null) return range.RangeExplanation;

        if (ContainsCall(time)) return "";

        return Phrase(ComplexitySimplifier.Simplify(time));
    }

    public static string UnknownText(string reason) =>
        "Unknown: The complexity cannot be easily determined because "
        + reason + ".";

    private static string Phrase(ComplexityExpression time) =>
        time switch
        {
            ConstantExpression => "Constant time",
            VariableExpression => "Linear time",
            LogExpression => "Logarithmic time",
            PowerExpression
            {
                Exponent: ConstantExpression { Value: 2 }
            } => "Quadratic time",
            PowerExpression
            {
                Exponent: ConstantExpression { Value: 3 }
            } => "Cubic time",
            ProductExpression p when IsLinearithmic(p) =>
                "Linearithmic time",
            ProductExpression p when IsMultilinear(p) =>
                p.Factors.Count == 2 ? "Bilinear time" : "Multilinear time",
            ProductExpression p when HasExponential(p) =>
                "Exponential time",
            ProductExpression p when HasFactorial(p) =>
                "Factorial time",
            ProductExpression p when HasBinomial(p) =>
                "Combinatorial time",
            SumExpression s when s.Terms.All(IsLinearOrConstant) =>
                "Linear time",
            PowerExpression
            {
                Base: ConstantExpression,
                Exponent: VariableExpression
            } => "Exponential time",
            FactorialExpression => "Factorial time",
            BinomialExpression => "Combinatorial time",
            _ => "",
        };

    private static bool IsLinearithmic(ProductExpression product)
    {
        var hasLog = product.Factors.Any(f => f is LogExpression);
        var hasVar = product.Factors.Any(f => f is VariableExpression);
        return hasLog && hasVar;
    }

    private static bool IsMultilinear(ProductExpression product) =>
        product.Factors.All(f => f is VariableExpression)
        && product.Factors.Count >= 2;

    private static bool IsLinearOrConstant(ComplexityExpression e) =>
        e is VariableExpression or ConstantExpression;

    private static bool HasExponential(ProductExpression p) =>
        p.Factors.Any(f => f is PowerExpression
        {
            Base: ConstantExpression,
            Exponent: VariableExpression
        });

    private static bool HasFactorial(ProductExpression p) =>
        p.Factors.Any(f => f is FactorialExpression);

    private static bool HasBinomial(ProductExpression p) =>
        p.Factors.Any(f => f is BinomialExpression);

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
