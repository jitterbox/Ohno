using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

internal static class RecurrenceAnalyzer
{
    public static ComposedCost? TrySolve(
        IMethodSymbol method,
        IOperation body,
        AnalysisState state)
    {
        var calls = FindRecursive(method, body).ToArray();
        if (calls.Length == 0) return null;

        var form = Classify(method, calls);
        return form switch
        {
            RecurrenceForm.Linear => Linear(method),
            RecurrenceForm.DivideAndConquer => DivideAndConquer(method),
            _ => Unresolved(method),
        };
    }

    private static IEnumerable<IInvocationOperation> FindRecursive(
        IMethodSymbol method, IOperation body)
    {
        return Walk(body).OfType<IInvocationOperation>()
            .Where(c => SymbolEqualityComparer.Default.Equals(
                c.TargetMethod.OriginalDefinition,
                method.OriginalDefinition));
    }

    private static RecurrenceForm Classify(
        IMethodSymbol method, IInvocationOperation[] calls)
    {
        var kinds = calls.Select(c => ArgumentKind(method, c)).ToArray();
        if (calls.Length == 1 && kinds[0] == ArgKind.MinusOne)
            return RecurrenceForm.Linear;
        if (calls.Length == 2 && kinds.All(k => k == ArgKind.Half))
            return RecurrenceForm.DivideAndConquer;
        return RecurrenceForm.Unknown;
    }

    private static ArgKind ArgumentKind(
        IMethodSymbol method, IInvocationOperation call)
    {
        if (method.Parameters.Length == 0 || call.Arguments.Length == 0)
            return ArgKind.Other;
        var index = 0;
        for (var i = 0; i < method.Parameters.Length
            && i < call.Arguments.Length; i++)
        {
            if (IsIntegral(method.Parameters[i].Type))
            {
                index = i;
                break;
            }
        }

        var arg = SizeResolver.Unwrap(call.Arguments[index].Value);
        return arg switch
        {
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Subtract,
                RightOperand: ILiteralOperation
                {
                    ConstantValue.Value: 1
                }
            } => ArgKind.MinusOne,
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Divide,
                RightOperand: ILiteralOperation
                {
                    ConstantValue.Value: 2
                }
            } => ArgKind.Half,
            _ => ArgKind.Other,
        };
    }

    private static ComposedCost Linear(IMethodSymbol method)
    {
        var n = Cx.Var("n");
        return new ComposedCost
        {
            Time = n,
            Space = n,
            Confidence = AnalysisConfidence.Medium,
            Evidence = ComplexityEvidence.Leaf(
                "recursion",
                $"{method.Name}(n-1) linear recurrence",
                n,
                null),
        };
    }

    private static ComposedCost DivideAndConquer(IMethodSymbol method)
    {
        var nLogN = Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("n")));
        return new ComposedCost
        {
            Time = nLogN,
            Space = Cx.Var("n"),
            Confidence = AnalysisConfidence.Medium,
            Evidence = ComplexityEvidence.Leaf(
                "recursion",
                $"{method.Name}: T(n)=2T(n/2)+O(n)",
                nLogN,
                null),
        };
    }

    private static ComposedCost Unresolved(IMethodSymbol method)
    {
        return new ComposedCost
        {
            Time = Cx.Call($"T({method.Name})"),
            Space = Cx.Unknown("recurrence"),
            Confidence = AnalysisConfidence.Unknown,
            Evidence = ComplexityEvidence.Leaf(
                "recursion",
                $"unresolved recurrence {method.Name}",
                Cx.Call($"T({method.Name})"),
                null),
            Warnings = new[]
            {
                new AnalysisWarning(
                    "Recurrence is not a recognized pattern; " +
                    "no complexity was invented."),
            },
        };
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

    private static bool IsIntegral(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_UInt32;

    private enum RecurrenceForm { Linear, DivideAndConquer, Unknown }

    private enum ArgKind { MinusOne, Half, Other }
}
