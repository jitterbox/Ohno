using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

internal static class HeapBoundDetector
{
    public static void Detect(IOperation body, AnalysisState state)
    {
        foreach (var operation in Walk(body))
        {
            if (operation is not IConditionalOperation cond) continue;
            if (!TryBound(cond.Condition, state, out var heap, out var bound))
                continue;
            if (!ContainsDequeue(cond.WhenTrue, heap)) continue;
            state.HeapBounds[heap] = bound;
        }
    }

    private static bool TryBound(
        IOperation condition,
        AnalysisState state,
        out ISymbol heap,
        out ComplexityExpression bound)
    {
        heap = null!;
        bound = Cx.One;
        if (SizeResolver.Unwrap(condition) is not IBinaryOperation binary)
            return false;
        if (binary.OperatorKind is not (
            BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual))
        {
            return false;
        }

        if (SizeResolver.Unwrap(binary.LeftOperand)
            is not IPropertyReferenceOperation prop)
        {
            return false;
        }

        if (prop.Property.Name != "Count") return false;
        var symbol = SizeResolver.TargetSymbol(prop.Instance);
        if (symbol is null) return false;
        heap = symbol;
        bound = SizeResolver.Resolve(binary.RightOperand, state);
        return true;
    }

    private static bool ContainsDequeue(IOperation? body, ISymbol heap)
    {
        if (body is null) return false;
        return Walk(body).Any(op =>
            op is IInvocationOperation call
            && call.TargetMethod.Name == "Dequeue"
            && SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(call.Instance), heap));
    }

    private static IEnumerable<IOperation> Walk(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var nested in Walk(child))
                yield return nested;
        }
    }
}
