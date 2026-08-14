using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

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
                return (Cx.Log(bound), $"logarithmic for ({bound})");
            return (bound, $"for ({bound})");
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
            if (IsHalvingWhile(loop))
                return (Cx.Log(bound), $"while log ({bound})");
            return (bound, $"while ({bound})");
        }

        return (Cx.Var("n"), "while (unknown bound)");
    }

    public static (ComplexityExpression Bound, string Label) Infer(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var bound = SizeResolver.Resolve(loop.Collection, state);
        return (bound, $"foreach ({ComplexityFormatter.Format(bound)})");
    }

    private static ComplexityExpression InferBinary(
        IBinaryOperation binary, AnalysisState state)
    {
        if (IsComparison(binary.OperatorKind))
        {
            var right = SizeResolver.Resolve(binary.RightOperand, state);
            var left = SizeResolver.Resolve(binary.LeftOperand, state);
            // j < i where i is a loop index: use the larger-looking side.
            if (IsLoopIndex(binary.RightOperand))
                return left is VariableExpression ? left : right;
            return right is ConstantExpression ? left : right;
        }

        return Cx.Var("n");
    }

    private static bool IsComparison(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual
            or BinaryOperatorKind.NotEquals;

    private static bool IsLoopIndex(IOperation operation) =>
        SizeResolver.Unwrap(operation) is ILocalReferenceOperation;

    private static bool IsLogarithmic(IForLoopOperation loop)
    {
        return loop.AtLoopBottom.Any(IsDoubling);
    }

    private static bool IsHalvingWhile(IWhileLoopOperation loop)
    {
        return Walk(loop.Body).Any(IsHalving);
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
            IExpressionStatementOperation e => IsHalving(e.Operation),
            _ => false,
        };
    }

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
