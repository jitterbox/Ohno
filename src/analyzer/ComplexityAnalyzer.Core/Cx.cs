using System.Collections.Immutable;

namespace ComplexityAnalyzer.Core;

/// <summary>
/// Factory helpers for building complexity expressions with structural
/// normalization (flattening, identity elimination, constant folding).
/// Full Big-O simplification lives in <see cref="ComplexitySimplifier"/>.
/// </summary>
public static class Cx
{
    public static readonly ComplexityExpression One = new ConstantExpression(1);

    public static ComplexityExpression Constant(int value) =>
        value == 1 ? One : new ConstantExpression(value);

    public static ComplexityExpression Var(string name) => new VariableExpression(name);

    public static ComplexityExpression Log(ComplexityExpression inner) =>
        inner is ConstantExpression ? One : new LogExpression(inner);

    public static ComplexityExpression Factorial(ComplexityExpression inner) =>
        new FactorialExpression(inner);

    public static ComplexityExpression Unknown(string reason) =>
        new UnknownExpression(reason);

    public static ComplexityExpression Call(string functionName) =>
        new FunctionCostExpression(functionName);

    public static ComplexityExpression Pow(ComplexityExpression baseExpr, ComplexityExpression exponent)
    {
        if (exponent is ConstantExpression { Value: 0 }) return One;
        if (exponent is ConstantExpression { Value: 1 }) return baseExpr;
        if (baseExpr is ConstantExpression { Value: 1 }) return One;
        // (n^a)^b => n^(a*b) when both exponents are constants.
        if (baseExpr is PowerExpression { Base: var innerBase, Exponent: ConstantExpression a }
            && exponent is ConstantExpression b)
        {
            return new PowerExpression(innerBase, Constant(a.Value * b.Value));
        }

        return new PowerExpression(baseExpr, exponent);
    }

    public static ComplexityExpression Add(params ComplexityExpression[] terms) =>
        Add((IEnumerable<ComplexityExpression>)terms);

    public static ComplexityExpression Add(IEnumerable<ComplexityExpression> terms)
    {
        var flat = ImmutableArray.CreateBuilder<ComplexityExpression>();
        foreach (var term in terms)
        {
            if (term is ConstantExpression { Value: 0 }) continue;
            if (term is SumExpression sum) flat.AddRange(sum.Terms);
            else flat.Add(term);
        }

        return flat.Count switch
        {
            0 => One,
            1 => flat[0],
            _ => new SumExpression(flat.ToImmutable()),
        };
    }

    public static ComplexityExpression Mul(params ComplexityExpression[] factors) =>
        Mul((IEnumerable<ComplexityExpression>)factors);

    public static ComplexityExpression Mul(IEnumerable<ComplexityExpression> factors)
    {
        var flat = ImmutableArray.CreateBuilder<ComplexityExpression>();
        var constant = 1;
        foreach (var factor in factors)
        {
            switch (factor)
            {
                case ConstantExpression { Value: 0 }:
                    return One; // A zero-iteration loop costs nothing asymptotically.
                case ConstantExpression c:
                    constant *= c.Value;
                    break;
                case ProductExpression product:
                    flat.AddRange(product.Factors);
                    break;
                default:
                    flat.Add(factor);
                    break;
            }
        }

        // Combine same-base powers with constant exponents: n^2 * n => n^3.
        var combined = CombinePowers(flat);
        if (constant != 1) combined.Insert(0, Constant(constant));

        return combined.Count switch
        {
            0 => One,
            1 => combined[0],
            _ => new ProductExpression(combined.ToImmutable()),
        };
    }

    private static ImmutableArray<ComplexityExpression>.Builder CombinePowers(
        ImmutableArray<ComplexityExpression>.Builder factors)
    {
        var result = ImmutableArray.CreateBuilder<ComplexityExpression>();
        // Insertion-ordered map: results must be deterministic.
        var powers = new List<(string Key, ComplexityExpression Base, int Exponent)>();
        var index = new Dictionary<string, int>();

        foreach (var factor in factors)
        {
            var (key, baseExpr, increment) = factor switch
            {
                VariableExpression v => (v.Name, (ComplexityExpression)v, 1),
                PowerExpression { Base: VariableExpression v, Exponent: ConstantExpression e }
                    => (v.Name, (ComplexityExpression)v, e.Value),
                _ => (null, factor, 0),
            };

            if (key is null)
            {
                result.Add(factor);
                continue;
            }

            if (index.TryGetValue(key, out var i))
            {
                var existing = powers[i];
                powers[i] = (existing.Key, existing.Base, existing.Exponent + increment);
            }
            else
            {
                index[key] = powers.Count;
                powers.Add((key, baseExpr, increment));
            }
        }

        foreach (var (_, baseExpr, exponent) in powers)
        {
            result.Add(exponent == 1 ? baseExpr : new PowerExpression(baseExpr, Constant(exponent)));
        }

        return result;
    }
}
