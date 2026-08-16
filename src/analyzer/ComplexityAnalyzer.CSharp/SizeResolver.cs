using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Maps an <see cref="IOperation"/> to a symbolic size (n, m, k, …).
/// </summary>
/// <remarks>
/// Rectangular arrays
/// (<see href="https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/arrays">arrays</see>)
/// multiply every <see cref="IArrayCreationOperation.DimensionSizes"/>
/// entry: <c>new T[n, n]</c> is n² cells, including zero-init time.
/// Jagged <c>new T[n][]</c> is one dimension (n references).
/// <see cref="IArrayElementReferenceOperation"/> on a collection-typed
/// element introduces a fresh degree dimension; that is not a proven
/// |E|. Implicit array literals with no dimension sizes are Θ(1).
/// Unresolved operations fall back to n — a guess, not a proof.
/// </remarks>
internal static class SizeResolver
{
    public static ComplexityExpression Resolve(
        IOperation? operation, AnalysisState state)
    {
        return Unwrap(operation) switch
        {
            IParameterReferenceOperation p => state.SizeOf(p.Parameter),
            ILocalReferenceOperation l => LocalSize(l, state),
            IFieldReferenceOperation f => FromField(f, state),
            IPropertyReferenceOperation prop => FromProperty(prop, state),
            IArrayCreationOperation a => FromArrayCreation(a, state),
            IArrayElementReferenceOperation e =>
                FromArrayElement(e, state),
            ILiteralOperation => Cx.One,
            IBinaryOperation b => FromBinary(b, state),
            IConversionOperation c => Resolve(c.Operand, state),
            IInvocationOperation inv => FromInvocation(inv, state),
            IObjectCreationOperation => Cx.One,
            _ => Cx.Var("n"),
        };
    }

    /// <summary>
    /// A fixed-size scalar — a constant or static readonly integral —
    /// is Θ(1), not an input dimension. <c>Repeat(int.MaxValue, n)</c>
    /// sizes by <c>n</c>, not <c>MaxValue</c>. A collection field is
    /// different: even a static readonly dictionary still has a size,
    /// so it keeps its own dimension rather than collapsing to 1.
    /// </summary>
    private static ComplexityExpression FromField(
        IFieldReferenceOperation field, AnalysisState state)
    {
        var isScalar =
            field.Field.IsConst
            || (field.Field.IsReadOnly && field.Field.IsStatic);
        if (isScalar && !DimensionInferrer.IsCollection(field.Type))
            return Cx.One;

        return state.SizeOf(field.Field);
    }

    private static ComplexityExpression LocalSize(
        ILocalReferenceOperation local, AnalysisState state)
    {
        if (state.LoopIndices.Contains(local.Local))
            return state.CurrentLoopBound ?? Cx.Var("n");
        return state.SizeOf(local.Local);
    }

    public static ISymbol? TargetSymbol(IOperation? operation)
    {
        return Unwrap(operation) switch
        {
            IParameterReferenceOperation p => p.Parameter,
            ILocalReferenceOperation l => l.Local,
            IFieldReferenceOperation f => f.Field,
            IPropertyReferenceOperation prop => TargetSymbol(prop.Instance),
            IArrayElementReferenceOperation e =>
                TargetSymbol(e.ArrayReference),
            IConversionOperation c => TargetSymbol(c.Operand),
            _ => null,
        };
    }

    public static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static ComplexityExpression FromBinary(
        IBinaryOperation binary, AnalysisState state)
    {
        if (binary.OperatorKind is not (
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract))
        {
            return Cx.Var("n");
        }

        var left = Resolve(binary.LeftOperand, state);
        var right = Resolve(binary.RightOperand, state);
        if (right is ConstantExpression) return left;
        if (left is ConstantExpression) return right;
        return left;
    }

    private static ComplexityExpression FromProperty(
        IPropertyReferenceOperation prop, AnalysisState state)
    {
        if (prop.Property.Name is "Count" or "Length" or "UnorderedItems")
            return Resolve(prop.Instance, state);
        return state.SizeOf(prop.Property);
    }

    private static ComplexityExpression FromInvocation(
        IInvocationOperation invocation, AnalysisState state)
    {
        if (invocation.TargetMethod.Name == "Repeat"
            && invocation.Arguments.Length >= 2)
        {
            return Cx.Mul(
                Resolve(invocation.Arguments[0].Value, state),
                Resolve(invocation.Arguments[1].Value, state));
        }

        if (TryTwoSource(invocation, state, out var combined))
            return combined;

        if (invocation.Instance is not null)
            return Resolve(invocation.Instance, state);
        if (invocation.Arguments.Length > 0)
            return Resolve(invocation.Arguments[0].Value, state);
        return Cx.Var("n");
    }

    /// <summary>
    /// Operators that consume two or more sequences are sized by every
    /// collection operand. <c>a.Concat(b)</c> is |a| + |b|;
    /// <c>string.Concat(a, b, c)</c> is |a| + |b| + |c|. Folding a
    /// later source into the first would drop an independent dimension.
    /// </summary>
    private static bool TryTwoSource(
        IInvocationOperation invocation,
        AnalysisState state,
        out ComplexityExpression size)
    {
        size = Cx.One;
        if (!IsTwoSourceOperator(invocation.TargetMethod.Name))
            return false;

        var parts = new List<ComplexityExpression>();
        if (invocation.Instance is not null
            && DimensionInferrer.IsCollection(invocation.Instance.Type))
        {
            parts.Add(Resolve(invocation.Instance, state));
        }

        foreach (var argument in invocation.Arguments)
        {
            if (!DimensionInferrer.IsCollection(argument.Value.Type))
                continue;
            parts.Add(Resolve(argument.Value, state));
        }

        if (parts.Count < 2) return false;
        size = Cx.Add(parts);
        return true;
    }

    internal static bool IsTwoSourceOperator(string name) =>
        name is "Concat" or "Union" or "UnionBy" or "Intersect"
            or "IntersectBy" or "Except" or "ExceptBy" or "Zip"
            or "SequenceEqual";

    private static ComplexityExpression FromArrayElement(
        IArrayElementReferenceOperation element, AnalysisState state)
    {
        if (!DimensionInferrer.IsCollection(element.Type))
            return Cx.One;
        var owner = TargetSymbol(element.ArrayReference);
        if (owner is null) return Cx.Var("n");
        return state.ElementSizeOf(owner, $"{owner.Name}[i].Count");
    }

    private static ComplexityExpression FromArrayCreation(
        IArrayCreationOperation creation, AnalysisState state)
    {
        if (creation.DimensionSizes.Length == 0)
            return creation.Initializer is null ? Cx.Var("n") : Cx.One;
        return Cx.Mul(
            creation.DimensionSizes.Select(d => Resolve(d, state)));
    }
}
