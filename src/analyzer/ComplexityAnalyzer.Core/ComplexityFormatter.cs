namespace ComplexityAnalyzer.Core;

/// <summary>
/// Default presentation-layer formatter. Produces output like
/// <c>Time O(n log k) · Space O(k) · High confidence</c>. Formatting is a
/// presentation concern layered over the symbolic result; alternate
/// formatters can be built without touching the analysis engine.
/// </summary>
public static class ComplexityFormatter
{
    public static string FormatBigO(ComplexityExpression expression) =>
        $"O({Format(expression)})";

    public static string Format(ComplexityExpression expression) =>
        expression switch
        {
            ConstantExpression c => c.Value.ToString(),
            VariableExpression v => v.Name,
            LogExpression log => $"log {FormatOperand(log.Inner)}",
            PowerExpression power => FormatPower(power),
            FactorialExpression factorial => $"{FormatOperand(factorial.Inner)}!",
            ProductExpression product => FormatProduct(product),
            SumExpression sum => string.Join(" + ", sum.Terms.Select(Format)),
            FunctionCostExpression call => $"C({call.FunctionName})",
            UnknownExpression => "unknown",
            _ => throw new ArgumentException($"Unknown expression type: {expression.GetType()}"),
        };

    /// <summary>The spec's headline format: Time O(...) · Space O(...) · X confidence.</summary>
    public static string FormatHeadline(ComplexityResult result) =>
        $"Time {FormatBigO(result.Time)} · Space {FormatBigO(result.AuxiliarySpace)} " +
        $"· {result.Confidence} confidence";

    private static string FormatProduct(ProductExpression product)
    {
        var ordered = product.Factors
            .OrderBy(ProductRank)
            .ThenBy(Format, StringComparer.Ordinal);
        return string.Join(' ', ordered.Select(FormatOperand));
    }

    private static int ProductRank(ComplexityExpression expression) =>
        expression switch
        {
            VariableExpression => 0,
            PowerExpression => 1,
            LogExpression => 2,
            FunctionCostExpression => 3,
            _ => 4,
        };

    private static string FormatPower(PowerExpression power) =>
        (power.Base, power.Exponent) switch
        {
            (_, ConstantExpression { Value: 2 }) => $"{FormatOperand(power.Base)}²",
            (_, ConstantExpression { Value: 3 }) => $"{FormatOperand(power.Base)}³",
            _ => $"{FormatOperand(power.Base)}^{FormatOperand(power.Exponent)}",
        };

    /// <summary>Parenthesizes compound operands inside products/logs/powers.</summary>
    private static string FormatOperand(ComplexityExpression expression) =>
        expression switch
        {
            SumExpression => $"({Format(expression)})",
            ProductExpression => $"({Format(expression)})",
            _ => Format(expression),
        };
}
