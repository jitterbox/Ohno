using System.Collections.Immutable;

namespace ComplexityAnalyzer.Core;

/// <summary>
/// Big-O simplification over symbolic complexity expressions:
/// distribution, constant elimination, dominance reduction, and canonical
/// ordering. Independent dimensions are never collapsed — O(n + m) stays
/// O(n + m) unless a relationship between n and m is known.
/// </summary>
public static class ComplexitySimplifier
{
    /// <summary>Safety cap on term expansion when distributing products over sums.</summary>
    private const int MaxDistributedTerms = 64;

    public static ComplexityExpression Simplify(ComplexityExpression expression)
    {
        var simplified = SimplifyNode(expression);
        return CanonicalOrder(simplified);
    }

    private static ComplexityExpression SimplifyNode(ComplexityExpression expression)
    {
        switch (expression)
        {
            case ConstantExpression:
            case VariableExpression:
            case UnknownExpression:
            case FunctionCostExpression:
                return expression;

            case FactorialExpression factorial:
                return Cx.Factorial(SimplifyNode(factorial.Inner));

            case LogExpression log:
                return SimplifyLog(SimplifyNode(log.Inner));

            case PowerExpression power:
                return Cx.Pow(SimplifyNode(power.Base), SimplifyNode(power.Exponent));

            case ProductExpression product:
                return SimplifyProduct(product.Factors.Select(SimplifyNode));

            case SumExpression sum:
                return SimplifySum(sum.Terms.Select(SimplifyNode));

            default:
                return expression;
        }
    }

    private static ComplexityExpression SimplifyLog(ComplexityExpression inner)
    {
        switch (inner)
        {
            case ConstantExpression:
                return Cx.One;
            // log(a * b) => log a + log b.
            case ProductExpression product:
                return SimplifySum(product.Factors.Select(f => SimplifyLog(f)));
            // log(n^a) => log n (constant factor drops out).
            case PowerExpression { Exponent: ConstantExpression } p:
                return Cx.Log(p.Base);
            default:
                return Cx.Log(inner);
        }
    }

    private static ComplexityExpression SimplifyProduct(
        IEnumerable<ComplexityExpression> factors)
    {
        var factorList = factors.ToList();

        // Distribute: n * (1 + log k) => n + n*log(k).
        var sumIndex = factorList.FindIndex(f => f is SumExpression);
        if (sumIndex >= 0)
        {
            var sum = (SumExpression)factorList[sumIndex];
            if (sum.Terms.Count <= MaxDistributedTerms)
            {
                var rest = factorList
                    .Where((_, i) => i != sumIndex)
                    .ToList();
                var distributed = sum.Terms
                    .Select(term => Cx.Mul(rest.Append(term)));
                return SimplifySum(distributed);
            }
        }

        var nonConstants = factorList
            .Where(f => f is not ConstantExpression)
            .ToList();
        return nonConstants.Count == 0
            ? Cx.One
            : Cx.Mul(nonConstants);
    }

    private static ComplexityExpression SimplifySum(IEnumerable<ComplexityExpression> terms)
    {
        // Flatten and structurally normalize each term first.
        var flat = new List<ComplexityExpression>();
        foreach (var term in terms)
        {
            var normalized = term is ProductExpression p
                ? SimplifyProduct(p.Factors)
                : term;

            if (normalized is SumExpression nested)
            {
                flat.AddRange(nested.Terms);
            }
            else if (normalized is not ConstantExpression { Value: 0 })
            {
                flat.Add(normalized);
            }
        }

        // Drop dominated and duplicate terms via pairwise monomial comparison.
        var kept = new List<(ComplexityExpression Expr, Monomial? Mono)>();
        foreach (var term in flat)
        {
            var monomial = Monomial.TryFrom(term);
            var dominated = false;

            for (var i = kept.Count - 1; i >= 0; i--)
            {
                if (monomial is null || kept[i].Mono is null) continue;

                if (kept[i].Mono!.Dominates(monomial))
                {
                    dominated = true;
                    break;
                }

                if (monomial.Dominates(kept[i].Mono!))
                {
                    kept.RemoveAt(i);
                }
            }

            if (!dominated) kept.Add((term, monomial));
        }

        return Cx.Add(kept.Select(k => k.Expr));
    }

    /// <summary>
    /// Canonical deterministic ordering of sum terms and product factors,
    /// so identical analyses always produce identical output.
    /// </summary>
    private static ComplexityExpression CanonicalOrder(ComplexityExpression expression)
    {
        switch (expression)
        {
            case SumExpression sum:
                var terms = sum.Terms
                    .Select(CanonicalOrder)
                    .OrderBy(ComplexityFormatter.Format, StringComparer.Ordinal)
                    .ToImmutableArray();
                return terms.Length == 1 ? terms[0] : new SumExpression(terms);

            case ProductExpression product:
                var factors = product.Factors
                    .Select(CanonicalOrder)
                    .OrderBy(ProductRank)
                    .ThenBy(ComplexityFormatter.Format, StringComparer.Ordinal)
                    .ToImmutableArray();
                return factors.Length == 1
                    ? factors[0]
                    : new ProductExpression(factors);

            case LogExpression log:
                return new LogExpression(CanonicalOrder(log.Inner));

            case PowerExpression power:
                return new PowerExpression(
                    CanonicalOrder(power.Base), CanonicalOrder(power.Exponent));

            case FactorialExpression factorial:
                return new FactorialExpression(CanonicalOrder(factorial.Inner));

            default:
                return expression;
        }
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
}
