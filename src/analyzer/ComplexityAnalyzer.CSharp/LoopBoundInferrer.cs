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
/// Logarithmic bounds require a doubling or halving update
/// (<c>*= 2</c>, <c>/= 2</c>) on <see cref="IForLoopOperation.AtLoopBottom"/>
/// or in the while body. <c>size = size / 2</c> and <c>size &gt;&gt;= 1</c>
/// are not currently recognized.
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
            var bound = InferBinary(binary, state);
            if (IsHalvingWhile(loop) || IsBinaryPartition(loop))
                return (Cx.Log(bound), $"while log ({Fmt(bound)})");
            if (WorklistBoundDetector.TryIterations(
                loop, state, out var nodes))
            {
                return (nodes, "while (heap worklist)");
            }

            if (TryFrontier(loop, state, out var frontier))
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
            var right = SizeResolver.Resolve(binary.RightOperand, state);
            var left = SizeResolver.Resolve(binary.LeftOperand, state);
            // j < i where i is a loop index: use the larger-looking side.
            if (IsLoopIndex(binary.RightOperand, state))
                return left is VariableExpression ? left : right;
            return right is ConstantExpression ? left : right;
        }

        return Cx.Var("n");
    }

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
            .SelectMany(Walk)
            .Any(op => IsDoubling(op) || IsHalving(op));
    }

    private static bool TryFrontier(
        IWhileLoopOperation loop,
        AnalysisState state,
        out ComplexityExpression bound)
    {
        bound = Cx.One;
        if (!IsCountPositive(loop.Condition)) return false;
        var visited = Walk(loop.Body)
            .OfType<ISimpleAssignmentOperation>()
            .Select(a => SizeResolver.Unwrap(a.Target))
            .OfType<IArrayElementReferenceOperation>()
            .Select(e => SizeResolver.TargetSymbol(e.ArrayReference))
            .FirstOrDefault(s => s is not null);
        if (visited is null) return false;
        bound = state.SizeOf(visited);
        return true;
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

    private static bool IsHalvingWhile(IWhileLoopOperation loop)
    {
        return Walk(loop.Body).Any(IsHalving);
    }

    public static bool IsProgressOnly(IOperation body)
    {
        foreach (var operation in DirectOps(body))
        {
            if (operation is IInvocationOperation) return false;
            if (operation is IObjectCreationOperation) return false;
            if (operation is IArrayCreationOperation) return false;
        }

        return true;
    }

    private static bool IsBinaryPartition(IWhileLoopOperation loop)
    {
        var midSplit = Walk(loop.Body).Any(IsMidSplit);
        var shrinks = Walk(loop.Body).Any(IsBoundShrink);
        return midSplit && shrinks;
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

    private static IEnumerable<IOperation> DirectOps(IOperation root)
    {
        yield return root;
        if (root is IForLoopOperation
            or IForEachLoopOperation
            or IWhileLoopOperation)
        {
            yield break;
        }

        foreach (var child in root.ChildOperations)
        {
            foreach (var nested in DirectOps(child))
                yield return nested;
        }
    }

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
