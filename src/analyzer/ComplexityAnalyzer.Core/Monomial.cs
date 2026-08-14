using System.Collections.Immutable;

namespace ComplexityAnalyzer.Core;

/// <summary>
/// Growth measure for one variable dimension. Compared lexicographically:
/// factorial dominates exponential, which dominates any polynomial,
/// and polynomials compare by (power, logPower).
/// </summary>
internal readonly record struct VariableMeasure(
    int Factorial,
    int Exponential,
    int Power,
    int LogPower) : IComparable<VariableMeasure>
{
    public static readonly VariableMeasure Zero = new(0, 0, 0, 0);

    public int CompareTo(VariableMeasure other)
    {
        var c = Factorial.CompareTo(other.Factorial);
        if (c != 0) return c;
        c = Exponential.CompareTo(other.Exponential);
        if (c != 0) return c;
        c = Power.CompareTo(other.Power);
        if (c != 0) return c;
        return LogPower.CompareTo(other.LogPower);
    }

    public static VariableMeasure operator +(VariableMeasure a, VariableMeasure b) =>
        new(a.Factorial + b.Factorial, a.Exponential + b.Exponential,
            a.Power + b.Power, a.LogPower + b.LogPower);
}

/// <summary>
/// Canonical monomial form of a product term, used for Big-O dominance
/// comparison. Opaque factors (unknown calls, unresolved costs) are tracked
/// separately and never collapse against dimensions they don't share.
/// </summary>
internal sealed record Monomial
{
    public required ImmutableSortedDictionary<string, VariableMeasure> Variables { get; init; }

    /// <summary>Opaque factor identities, e.g. "call:Process" or "unknown:...".</summary>
    public required ImmutableSortedSet<string> Opaques { get; init; }

    public bool IsConstant => Variables.Count == 0 && Opaques.Count == 0;

    /// <summary>
    /// Extracts the monomial form of an expression, or returns null when the
    /// expression has no monomial representation (e.g. an undistributed sum).
    /// </summary>
    public static Monomial? TryFrom(ComplexityExpression expression)
    {
        var variables = ImmutableSortedDictionary.CreateBuilder<string, VariableMeasure>();
        var opaques = ImmutableSortedSet.CreateBuilder<string>();

        if (!Accumulate(expression, variables, opaques)) return null;

        return new Monomial
        {
            Variables = variables.ToImmutable(),
            Opaques = opaques.ToImmutable(),
        };
    }

    private static bool Accumulate(
        ComplexityExpression expression,
        ImmutableSortedDictionary<string, VariableMeasure>.Builder variables,
        ImmutableSortedSet<string>.Builder opaques)
    {
        switch (expression)
        {
            case ConstantExpression:
                return true;

            case VariableExpression v:
                Add(v.Name, new VariableMeasure(0, 0, 1, 0), variables);
                return true;

            case LogExpression { Inner: VariableExpression v }:
                Add(v.Name, new VariableMeasure(0, 0, 0, 1), variables);
                return true;

            case LogExpression { Inner: PowerExpression { Base: VariableExpression v, Exponent: ConstantExpression } }:
                // log(n^a) == a * log(n); the constant factor drops out.
                Add(v.Name, new VariableMeasure(0, 0, 0, 1), variables);
                return true;

            case PowerExpression { Base: VariableExpression v, Exponent: ConstantExpression e }:
                Add(v.Name, new VariableMeasure(0, 0, e.Value, 0), variables);
                return true;

            case PowerExpression { Base: ConstantExpression, Exponent: VariableExpression v }:
                // b^n is exponential in n and dominates any polynomial in n.
                Add(v.Name, new VariableMeasure(0, 1, 0, 0), variables);
                return true;

            case FactorialExpression { Inner: VariableExpression v }:
                Add(v.Name, new VariableMeasure(1, 0, 0, 0), variables);
                return true;

            case ProductExpression product:
                foreach (var factor in product.Factors)
                {
                    if (!Accumulate(factor, variables, opaques)) return false;
                }

                return true;

            case BinomialExpression binomial:
                opaques.Add(
                    $"binomial:{ComplexityFormatter.Format(binomial)}");
                return true;

            case FunctionCostExpression call:
                opaques.Add($"call:{call.FunctionName}");
                return true;

            case UnknownExpression unknown:
                opaques.Add($"unknown:{unknown.Reason}");
                return true;

            default:
                return false;
        }
    }

    private static void Add(
        string name,
        VariableMeasure measure,
        ImmutableSortedDictionary<string, VariableMeasure>.Builder variables)
    {
        variables.TryGetValue(name, out var existing);
        variables[name] = existing + measure;
    }

    /// <summary>
    /// Asymptotic dominance: this term dominates <paramref name="other"/> when
    /// it grows at least as fast on every shared dimension (assuming all
    /// dimensions grow independently) and strictly faster on at least one.
    /// Opaque factors must be a superset — an unknown call is never absorbed
    /// by a term that doesn't also contain it. A strict opaque superset counts
    /// as strict growth, since any call costs at least constant time.
    /// </summary>
    public bool Dominates(Monomial other)
    {
        if (!Opaques.IsSupersetOf(other.Opaques)) return false;
        if (EquivalentTo(other)) return true;
        if (LogProductDominatesVariable(other)) return true;
        if (LogProductDominatesLogProduct(other)) return true;

        // Any real work (a dimension or an opaque call) dominates a constant.
        if (other.IsConstant) return true;
        if (IsConstant) return false;

        var anyStrict = Opaques.Count > other.Opaques.Count;
        foreach (var key in Variables.Keys.Union(other.Variables.Keys))
        {
            var mine = Variables.GetValueOrDefault(key, VariableMeasure.Zero);
            var theirs = other.Variables.GetValueOrDefault(key, VariableMeasure.Zero);
            var comparison = mine.CompareTo(theirs);
            if (comparison < 0) return false;
            if (comparison > 0) anyStrict = true;
        }

        return anyStrict;
    }

    /// <summary>
    /// n log k dominates k — the usual Top-K simplification
    /// O(n log k + k) => O(n log k).
    /// </summary>
    private bool LogProductDominatesVariable(Monomial other)
    {
        if (other.Opaques.Count > 0 || other.Variables.Count != 1)
            return false;
        var (name, measure) = other.Variables.Single();
        if (!measure.Equals(new VariableMeasure(0, 0, 1, 0))) return false;
        if (!Variables.TryGetValue(name, out var mine) || mine.LogPower < 1)
            return false;
        return Variables.Any(kv =>
            kv.Key != name && kv.Value.Power >= 1);
    }

    /// <summary>
    /// n log k dominates k log k when n covers the seeded lists
    /// (k-way merge: every list head is one of the n nodes).
    /// Does not absorb n log n into m log n — those dimensions
    /// are independent.
    /// </summary>
    private bool LogProductDominatesLogProduct(Monomial other)
    {
        if (other.Opaques.Count > 0 || other.Variables.Count != 1)
            return false;
        var (name, measure) = other.Variables.Single();
        if (name != "k") return false;
        if (!measure.Equals(new VariableMeasure(0, 0, 1, 1)))
            return false;
        if (!Variables.TryGetValue("k", out var mine) || mine.LogPower < 1)
            return false;
        return Variables.TryGetValue("n", out var cover)
            && cover.Power >= 1;
    }

    /// <summary>Structural equality of the underlying measure maps.</summary>
    public bool EquivalentTo(Monomial other) =>
        Opaques.SetEquals(other.Opaques)
        && Variables.Count == other.Variables.Count
        && Variables.All(kv => other.Variables.TryGetValue(kv.Key, out var m) && m.Equals(kv.Value));
}
