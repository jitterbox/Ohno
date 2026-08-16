using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Infers a symbolic iteration bound from a loop condition and update.
/// </summary>
/// <remarks>
/// Comparison loops use
/// <see href="https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements">iteration statements</see>.
/// Logarithmic bounds are recognized from a doubling or halving
/// update (<c>*= 2</c>, <c>/= 2</c>, <c>&gt;&gt;= 1</c>, or
/// <c>size = size / 2</c>) on <see cref="IForLoopOperation.AtLoopBottom"/>
/// or in the while body.
/// Null-terminated walks assume a finite acyclic chain.
/// A <c>while (queue.Count &gt; 0)</c> plus a <c>bool[]</c> write is
/// treated as a visited frontier (BFS), not an unbounded spin.
/// </remarks>
internal static class LoopBoundInferrer
{
    public static (ComplexityExpression Bound, string Label) Infer(
        IForLoopOperation loop, AnalysisState state)
    {
        var condition = SizeResolver.Unwrap(loop.Condition);
        if (condition is IBinaryOperation binary)
        {
            var bound = InferBinary(binary, state);
            if (IsLogarithmic(loop))
                return (Cx.Log(bound), $"for log ({Fmt(bound)})");
            return (bound, $"for ({Fmt(bound)})");
        }

        return (Cx.Var("n"), "for (unknown bound)");
    }

    public static (ComplexityExpression Bound, string Label) Infer(
        IWhileLoopOperation loop, AnalysisState state)
    {
        var condition = SizeResolver.Unwrap(loop.Condition);
        if (condition is IBinaryOperation binary)
        {
            var shape = ShapeOf(loop, state);
            var bound = InferBinary(binary, state);
            if (shape.Halves || (shape.MidSplits && shape.ShrinksBound))
                return (Cx.Log(bound), $"while log ({Fmt(bound)})");
            if (WorklistBoundDetector.TryIterations(
                loop, state, out var nodes))
            {
                return (nodes, "while (heap worklist)");
            }

            if (TryFrontier(loop, shape, state, out var frontier))
                return (frontier, "while (visited frontier)");

            if (IsNullTerminated(condition))
                return (bound, "while (null-terminated)");
            return (bound, $"while ({Fmt(bound)})");
        }

        if (IsNullTerminated(condition))
            return (Cx.Var("n"), "while (null-terminated)");
        return (Cx.Var("n"), "while (unknown bound)");
    }

    public static (ComplexityExpression Bound, string Label) Infer(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var bound = SizeResolver.Resolve(loop.Collection, state);
        return (bound, $"foreach ({Fmt(bound)})");
    }

    private static ComplexityExpression InferBinary(
        IBinaryOperation binary, AnalysisState state)
    {
        if (IsComparison(binary.OperatorKind))
        {
            // A literal *upper* bound on a constant-step counter is a
            // fixed count: `for (j = 0; j < 8; j++)`. A literal
            // *floor* (`j >= 0`) is not — iterations then follow
            // wherever the counter was seeded, which may be the
            // enclosing index (`j = i - 1`).
            //
            // `while (size > 1) size >>= 1` also compares against a
            // literal, but it is not a stepped counter; halving
            // detection downstream keeps that logarithmic.
            if (IsFixedLiteralCeiling(binary, state))
                return Cx.One;

            var right = SizeResolver.Resolve(binary.RightOperand, state);
            var left = SizeResolver.Resolve(binary.LeftOperand, state);
            // j < i where i is a loop index: use the larger-looking side.
            if (IsLoopIndex(binary.RightOperand, state))
                return left is VariableExpression ? left : right;
            return right is ConstantExpression ? left : right;
        }

        return Cx.Var("n");
    }

    private static bool IsFixedLiteralCeiling(
        IBinaryOperation binary, AnalysisState state)
    {
        if (binary.OperatorKind is not (
            BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual))
        {
            return false;
        }

        return IsLiteral(binary.RightOperand)
            && IsSteppedCounter(binary.LeftOperand, state);
    }

    private static bool IsLiteral(IOperation operation) =>
        SizeResolver.Unwrap(operation) is ILiteralOperation
        {
            ConstantValue.HasValue: true,
        };

    /// <summary>
    /// A local the increment scan saw moving by a constant step. This
    /// is the strict form of <see cref="IsLoopIndex"/>, which also
    /// accepts any integral local as a fallback — too loose to decide
    /// that a loop terminates in constant time.
    /// </summary>
    private static bool IsSteppedCounter(
        IOperation operation, AnalysisState state) =>
        SizeResolver.Unwrap(operation) is ILocalReferenceOperation local
        && state.LoopIndices.Contains(local.Local);

    private static string Fmt(ComplexityExpression expression) =>
        ComplexityFormatter.Format(expression);

    private static bool IsNullTerminated(IOperation? condition) =>
        SizeResolver.Unwrap(condition) switch
        {
            IIsPatternOperation => true,
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } u =>
                IsNullTerminated(u.Operand),
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd
                    or BinaryOperatorKind.ConditionalOr
            } b =>
                IsNullTerminated(b.LeftOperand)
                || IsNullTerminated(b.RightOperand),
            _ => false,
        };

    private static bool IsComparison(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual
            or BinaryOperatorKind.NotEquals;

    private static bool IsLoopIndex(
        IOperation operation, AnalysisState state)
    {
        if (SizeResolver.Unwrap(operation)
            is not ILocalReferenceOperation local)
        {
            return false;
        }

        if (state.LoopIndices.Contains(local.Local)) return true;
        return local.Local.Type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_UInt32;
    }

    private static bool IsLogarithmic(IForLoopOperation loop)
    {
        if (loop.AtLoopBottom.Any(IsDoubling)
            || loop.AtLoopBottom.Any(IsHalving))
        {
            return true;
        }

        return loop.ChildOperations
            .Where(op => op != loop.Body && op != loop.Condition)
            .SelectMany(OperationTree.SelfAndDescendants)
            .Any(op => IsDoubling(op) || IsHalving(op));
    }

    private static bool TryFrontier(
        IWhileLoopOperation loop,
        LoopShape shape,
        AnalysisState state,
        out ComplexityExpression bound)
    {
        bound = Cx.One;
        if (!IsCountPositive(loop.Condition)) return false;
        if (shape.VisitedArray is null) return false;
        // The symbol is structural and cached; its size is not, so it
        // is resolved against the current state on every call.
        bound = state.SizeOf(shape.VisitedArray);
        return true;
    }

    /// <summary>
    /// Gathers every shape question about a loop body in one pass, and
    /// caches it. These used to be up to four independent full walks of
    /// the same sub-tree per call, and <c>Infer</c> is called both by
    /// the cardinality pass and by the cost walk.
    /// </summary>
    private static LoopShape ShapeOf(
        IWhileLoopOperation loop, AnalysisState state)
    {
        if (state.LoopShapes.TryGetValue(loop, out var cached))
            return cached;

        var halves = false;
        var midSplits = false;
        var shrinks = false;
        ISymbol? visited = null;

        foreach (var op in OperationTree.SelfAndDescendants(loop.Body))
        {
            if (!halves && IsHalving(op)) halves = true;
            if (!midSplits && IsMidSplit(op)) midSplits = true;
            if (!shrinks && IsBoundShrink(op)) shrinks = true;
            if (visited is null) visited = VisitTarget(op);
        }

        var shape = new LoopShape(halves, midSplits, shrinks, visited);
        state.LoopShapes[loop] = shape;
        return shape;
    }

    /// <summary>
    /// The array a visit mark is written into, if this operation is
    /// such a write. First hit wins, matching the previous walk.
    /// </summary>
    private static ISymbol? VisitTarget(IOperation operation)
    {
        if (operation is not ISimpleAssignmentOperation assign)
            return null;
        if (SizeResolver.Unwrap(assign.Target)
            is not IArrayElementReferenceOperation element)
        {
            return null;
        }

        return SizeResolver.TargetSymbol(element.ArrayReference);
    }

    private static bool IsCountPositive(IOperation? condition)
    {
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
        return left is IPropertyReferenceOperation
        {
            Property.Name: "Count"
        };
    }


    /// <summary>
    /// Whether the inner loop's counter is re-seeded by the enclosing
    /// loop, which is what separates a two-pointer scan from a
    /// quadratic one.
    /// </summary>
    /// <remarks>
    /// The amortized-pointer rule says an inner <c>while</c> that only
    /// advances a pointer costs O(1) per outer step, because across the
    /// whole outer loop the pointer moves at most n times in total.
    /// That argument holds precisely while the pointer keeps its
    /// position between outer iterations.
    /// <para>
    /// Insertion sort breaks it: <c>j = i - 1</c> puts the counter back
    /// every time, so the inner walk can run its full length on each
    /// outer step and the loop really is quadratic. A reset is visible
    /// in the source, so this is a proof, not a guess.
    /// </para>
    /// </remarks>
    public static bool ResetsCounter(
        IOperation loop, AnalysisState state)
    {
        if (state.CurrentLoopBody is not { } outer) return false;
        var counters = CounterSymbols(LoopCondition(loop));
        if (counters.Count == 0) return false;
        return OutsideInnerLoop(outer, loop)
            .Any(op => SeedsCounter(op, counters));
    }

    private static IOperation? LoopCondition(IOperation loop) =>
        loop switch
        {
            IWhileLoopOperation w => w.Condition,
            IForLoopOperation f => f.Condition,
            _ => null,
        };

    private static bool SeedsCounter(
        IOperation operation, HashSet<ISymbol> counters) =>
        operation switch
        {
            ISimpleAssignmentOperation assign
                when Targets(assign.Target, counters) => true,
            IVariableDeclaratorOperation declarator
                when counters.Contains(declarator.Symbol) => true,
            _ => false,
        };

    private static bool Targets(
        IOperation target, HashSet<ISymbol> counters)
    {
        var symbol = SizeResolver.TargetSymbol(target);
        return symbol is not null && counters.Contains(symbol);
    }

    /// <summary>Locals the loop condition tests.</summary>
    private static HashSet<ISymbol> CounterSymbols(IOperation? condition)
    {
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (condition is null) return symbols;
        foreach (var op in OperationTree.SelfAndDescendants(condition))
        {
            if (op is ILocalReferenceOperation local)
                symbols.Add(local.Local);
        }

        return symbols;
    }

    /// <summary>
    /// The enclosing body without the inner loop's own subtree, so the
    /// inner loop's stepping does not read as a reset.
    /// </summary>
    private static IEnumerable<IOperation> OutsideInnerLoop(
        IOperation outer, IOperation inner)
    {
        var stack = new Stack<IOperation>();
        stack.Push(outer);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (ReferenceEquals(current, inner)) continue;
            yield return current;
            foreach (var child in current.ChildOperations)
                stack.Push(child);
        }
    }

    public static bool IsProgressOnly(IOperation body)
    {
        foreach (var operation in OperationTree.WithinLoopLevel(body))
        {
            if (operation is IInvocationOperation) return false;
            if (operation is IObjectCreationOperation) return false;
            if (operation is IArrayCreationOperation) return false;
        }

        return true;
    }


    private static bool IsMidSplit(IOperation operation) =>
        SizeResolver.Unwrap(operation) is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Divide
        } divide
        && IsTwo(divide.RightOperand);

    private static bool IsBoundShrink(IOperation operation) =>
        SizeResolver.Unwrap(operation) is ISimpleAssignmentOperation assign
        && assign.Target is ILocalReferenceOperation
        && SizeResolver.Unwrap(assign.Value) is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add
                or BinaryOperatorKind.Subtract
        };

    private static bool IsDoubling(IOperation operation)
    {
        return SizeResolver.Unwrap(operation) switch
        {
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Multiply
            } c => IsTwo(c.Value),
            IExpressionStatementOperation e => IsDoubling(e.Operation),
            _ => false,
        };
    }

    private static bool IsHalving(IOperation operation)
    {
        return SizeResolver.Unwrap(operation) switch
        {
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Divide
            } c => IsTwo(c.Value),
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.RightShift
            } s => IsOne(s.Value),
            ISimpleAssignmentOperation assign
                when SizeResolver.Unwrap(assign.Value)
                    is IBinaryOperation
                    {
                        OperatorKind: BinaryOperatorKind.Divide
                    } d => IsTwo(d.RightOperand),
            IExpressionStatementOperation e => IsHalving(e.Operation),
            _ => false,
        };
    }

    private static bool IsOne(IOperation operation) =>
        SizeResolver.Unwrap(operation) is ILiteralOperation
        {
            ConstantValue.HasValue: true,
            ConstantValue.Value: 1
        };

    private static bool IsTwo(IOperation operation) =>
        SizeResolver.Unwrap(operation) is ILiteralOperation
        {
            ConstantValue.HasValue: true,
            ConstantValue.Value: 2
        };

}
