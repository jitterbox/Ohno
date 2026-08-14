using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Refill worklist: <c>while (c.Count &gt; 0)</c> plus Enqueue/Push
/// in the body. Iterations are not the current Count.
/// </summary>
internal static class WorklistBoundDetector
{
    public static void Detect(IOperation body, AnalysisState state)
    {
        foreach (var loop in Loops(body))
        {
            if (!TryCollection(loop.Condition, out var work))
                continue;
            if (!HasShrink(loop.Body, work)) continue;
            if (!HasGrow(loop.Body, work)) continue;
            Bind(work, loop, body, state);
        }
    }

    public static bool TryIterations(
        IWhileLoopOperation loop,
        AnalysisState state,
        out ComplexityExpression bound)
    {
        bound = Cx.One;
        if (!TryCollection(loop.Condition, out var work))
            return false;
        return state.WorklistBounds.TryGetValue(work, out bound!);
    }

    private static void Bind(
        ISymbol work,
        IWhileLoopOperation loop,
        IOperation body,
        AnalysisState state)
    {
        if (state.WorklistBounds.ContainsKey(work)) return;
        var seed = SeedOf(work, state);
        var visit = VisitSize(loop.Body, state);
        var successor = HasSuccessorGrow(loop.Body, work);
        var netDecrease = IsNetDecrease(loop);
        var edges = EdgeSize(loop.Body, state);

        if (visit is null && !successor && !netDecrease)
        {
            state.WorklistBounds[work] = Cx.Unknown("worklist");
            state.Note(
                AnalysisConfidence.Unknown,
                "A refill worklist has no visit mark; iterations "
                + "are not bounded by Count.");
            return;
        }

        if (successor)
        {
            BindLinked(work, seed, body, state);
            return;
        }

        if (netDecrease)
        {
            state.WorklistBounds[work] = seed;
            state.HeapBounds[work] = seed;
            state.Sizes[work] = seed;
            return;
        }

        BindGraph(work, visit!, edges, state);
    }

    private static void BindLinked(
        ISymbol work,
        ComplexityExpression seed,
        IOperation body,
        AnalysisState state)
    {
        var seeded = SeedSize(body, work, state) ?? seed;
        state.HeapBounds[work] = seeded;
        state.Sizes[work] = seeded;
        var nodes = DimensionInferrer.Fresh(
            state, "nodes across lists");
        state.WorklistBounds[work] = nodes;
        state.Note(
            AnalysisConfidence.Medium,
            "Worklist walks linked-list successors; heap size "
            + "stays the seeded count and iterations count "
            + "every node.");
    }

    private static void BindGraph(
        ISymbol work,
        ComplexityExpression visit,
        ComplexityExpression? edges,
        AnalysisState state)
    {
        state.HeapBounds[work] = visit;
        state.Sizes[work] = visit;
        if (edges is not null)
        {
            state.WorklistBounds[work] = Cx.Add(visit, edges);
            state.FlattenedAdj.UnionWith(state.EdgeCounts.Keys);
            state.Note(
                AnalysisConfidence.Medium,
                "Graph worklist counts vertices plus edges; "
                + "adjacency foreach is not multiplied again.");
            return;
        }

        state.WorklistBounds[work] = visit;
        state.FrontierBound = visit;
        state.Note(
            AnalysisConfidence.Medium,
            "Worklist iterations follow the visited set, not "
            + "the current Count.");
    }

    private static ComplexityExpression SeedOf(
        ISymbol work, AnalysisState state)
    {
        if (state.HeapBounds.TryGetValue(work, out var heap))
            return heap;
        if (state.Cardinalities.TryGetValue(work, out var card))
            return card.Max;
        return state.SizeOf(work);
    }

    private static ComplexityExpression? VisitSize(
        IOperation body, AnalysisState state)
    {
        foreach (var op in Walk(body))
        {
            var target = WrittenTarget(op);
            if (target is not IArrayElementReferenceOperation element)
                continue;
            var array = SizeResolver.TargetSymbol(element.ArrayReference);
            if (array is null) continue;
            return state.SizeOf(array);
        }

        return null;
    }

    private static IOperation? WrittenTarget(IOperation operation) =>
        operation switch
        {
            ISimpleAssignmentOperation a =>
                SizeResolver.Unwrap(a.Target),
            ICompoundAssignmentOperation c =>
                SizeResolver.Unwrap(c.Target),
            IIncrementOrDecrementOperation i =>
                SizeResolver.Unwrap(i.Target),
            _ => null,
        };

    private static ComplexityExpression? EdgeSize(
        IOperation body, AnalysisState state)
    {
        foreach (var loop in Walk(body).OfType<IForEachLoopOperation>())
        {
            var owner = ArrayOwner(loop.Collection);
            if (owner is null) continue;
            if (state.EdgeCounts.TryGetValue(owner, out var edges))
                return edges;
        }

        return null;
    }

    private static ISymbol? ArrayOwner(IOperation? collection)
    {
        var op = SizeResolver.Unwrap(collection);
        if (op is IArrayElementReferenceOperation element)
            return SizeResolver.TargetSymbol(element.ArrayReference);
        return null;
    }

    private static ComplexityExpression? SeedSize(
        IOperation body, ISymbol work, AnalysisState state)
    {
        foreach (var loop in Walk(body).OfType<IForEachLoopOperation>())
        {
            if (!GrowsHeads(loop.Body, work)) continue;
            return SizeResolver.Resolve(loop.Collection, state);
        }

        return null;
    }

    private static bool TryCollection(
        IOperation? condition, out ISymbol work)
    {
        work = null!;
        if (SizeResolver.Unwrap(condition) is not IBinaryOperation binary)
            return false;
        if (binary.OperatorKind is not (
            BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual
            or BinaryOperatorKind.NotEquals))
        {
            return false;
        }

        var left = SizeResolver.Unwrap(binary.LeftOperand);
        if (left is not IPropertyReferenceOperation
            { Property.Name: "Count" } prop)
        {
            return false;
        }

        var symbol = SizeResolver.TargetSymbol(prop.Instance);
        if (symbol is null) return false;
        work = symbol;
        return true;
    }

    private static bool HasShrink(IOperation body, ISymbol work) =>
        Calls(body, work).Any(c => IsShrink(c.TargetMethod.Name));

    private static bool HasGrow(IOperation body, ISymbol work) =>
        Calls(body, work).Any(c => IsGrow(c.TargetMethod.Name));

    private static bool HasSuccessorGrow(
        IOperation body, ISymbol work) =>
        Calls(body, work).Any(c =>
            IsGrow(c.TargetMethod.Name)
            && c.Arguments.Length > 0
            && IsSuccessor(c.Arguments[0].Value));

    private static bool GrowsHeads(IOperation body, ISymbol work) =>
        Calls(body, work).Any(c =>
            IsGrow(c.TargetMethod.Name)
            && c.Arguments.Length > 0
            && !IsSuccessor(c.Arguments[0].Value));

    private static bool IsNetDecrease(IWhileLoopOperation loop)
    {
        if (SizeResolver.Unwrap(loop.Condition)
            is not IBinaryOperation binary)
        {
            return false;
        }

        var right = SizeResolver.Unwrap(binary.RightOperand);
        if (right is not ILiteralOperation
            { ConstantValue.Value: 1 })
        {
            return false;
        }

        return CountShrinks(loop.Body) > CountGrows(loop.Body);
    }

    private static int CountShrinks(IOperation body) =>
        Walk(body).OfType<IInvocationOperation>()
            .Count(c => IsShrink(c.TargetMethod.Name));

    private static int CountGrows(IOperation body) =>
        Walk(body).OfType<IInvocationOperation>()
            .Count(c => IsGrow(c.TargetMethod.Name));

    private static bool IsSuccessor(IOperation? value)
    {
        var op = SizeResolver.Unwrap(value);
        return op switch
        {
            IFieldReferenceOperation f => IsNext(f.Field.Name),
            IPropertyReferenceOperation p => IsNext(p.Property.Name),
            _ => false,
        };
    }

    private static bool IsNext(string name) =>
        name.Equals("next", StringComparison.OrdinalIgnoreCase);

    private static bool IsGrow(string name) =>
        name is "Enqueue" or "Push" or "Add";

    private static bool IsShrink(string name) =>
        name is "Dequeue" or "Pop" or "TryDequeue" or "TryPop"
            or "Remove" or "RemoveAt";

    private static IEnumerable<IInvocationOperation> Calls(
        IOperation body, ISymbol work) =>
        Walk(body).OfType<IInvocationOperation>().Where(c =>
            SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(c.Instance), work));

    private static IEnumerable<IWhileLoopOperation> Loops(
        IOperation root) =>
        Walk(root).OfType<IWhileLoopOperation>();

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
