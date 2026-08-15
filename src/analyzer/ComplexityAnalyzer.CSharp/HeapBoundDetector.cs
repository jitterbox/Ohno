using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Detects <c>if (collection.Count &gt; k) collection.Dequeue()</c>
/// and records a heap/window size of k.
/// </summary>
/// <remarks>
/// Used for bounded priority queues and sliding windows. A different
/// eviction shape (<c>RemoveAt(0)</c>, <c>TryDequeue</c> only, or a
/// cap on <c>Enqueue</c>) will not match. Confidence is Medium.
/// </remarks>
internal static class HeapBoundDetector
{
    public static void Detect(IOperation body, AnalysisState state)
    {
        foreach (var operation in OperationTree.SelfAndDescendants(body))
        {
            if (operation is not IConditionalOperation cond) continue;
            if (!TryBound(cond.Condition, state, out var heap, out var bound))
                continue;
            if (!ContainsShrink(cond.WhenTrue, heap)) continue;
            state.HeapBounds[heap] = bound;
            state.Sizes[heap] = bound;
            state.Card(heap).Max = bound;
            state.Note(
                AnalysisConfidence.Medium,
                "Collection size is assumed bounded by a Count > k "
                + "+ Dequeue check; a different bound shape may miss this.");
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

    private static bool ContainsShrink(IOperation? body, ISymbol heap)
    {
        if (body is null) return false;
        return OperationTree.SelfAndDescendants(body).OfType<IInvocationOperation>().Any(call =>
            IsShrink(call.TargetMethod.Name)
            && SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(call.Instance), heap));
    }

    private static bool IsShrink(string name) =>
        name is "Dequeue" or "TryDequeue" or "Pop" or "TryPop"
            or "RemoveAt" or "Remove";

}
