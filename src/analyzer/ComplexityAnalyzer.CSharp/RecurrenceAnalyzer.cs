using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Classifies a recursive method into a closed form when the call
/// pattern is a known idiom. Does not solve arbitrary recurrences.
/// </summary>
/// <remarks>
/// Recognized shapes: linear <c>T(n)=T(n-1)+O(1)</c>, divide-and-conquer
/// <c>T(n)=2T(n/2)+O(n)</c>, exclusive mid-split (binary search),
/// sequential <c>n-1</c>/<c>n-2</c> (Fibonacci), 2D memo tables,
/// subset / permutation / combination generation, and graph walks
/// over a visited array.
/// Edge cases that stay unresolved: mutual recursion, delegates,
/// <c>n -= 2</c> only, and helpers that hide the recursive call.
/// Confidence is Medium because an equivalent algorithm with a
/// different control-flow shape may not match.
/// </remarks>
internal static class RecurrenceAnalyzer
{
    public static ComposedCost? TrySolve(
        IMethodSymbol method,
        IOperation body,
        AnalysisState state)
    {
        var calls = FindRecursive(method, body).ToArray();
        if (calls.Length == 0) return null;

        if (IsBinarySearch(body, calls))
            return Solved(method, state, Cx.Log(Cx.Var("n")),
                Cx.Log(Cx.Var("n")), "binary-search recursion");

        if (TryMemoized(body, state, out var states))
            return Solved(method, state, states, Cx.Var("n"),
                "memoized recursion");

        if (IsSubsetGeneration(body, calls))
        {
            var n = Cx.Var("n");
            var cost = Cx.Mul(n, Cx.Pow(Cx.Constant(2), n));
            return Solved(method, state, cost, cost, "subset generation");
        }

        if (TryPermutationOrCombination(
            body, calls, state, out var genTime, out var genSpace))
        {
            return Solved(method, state, genTime, genSpace,
                "combinatorial generation");
        }

        if (TryGraphWalk(body, state, out var gTime, out var gSpace))
            return Solved(method, state, gTime, gSpace, "graph traversal");

        if (IsBranchingDecrease(method, calls))
        {
            var n = Cx.Var("n");
            return Solved(
                method, state, Cx.Pow(Cx.Constant(2), n), n,
                "branching recursion");
        }

        return Classify(method, calls) switch
        {
            RecurrenceForm.Linear => Linear(method, state),
            RecurrenceForm.DivideAndConquer =>
                DivideAndConquer(method, state),
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

    private static bool IsBinarySearch(
        IOperation body, IInvocationOperation[] calls)
    {
        if (calls.Length is < 1 or > 2) return false;
        if (!calls.All(IsExclusiveBranch)) return false;
        return Walk(body).Any(IsMidSplit);
    }

    private static bool IsExclusiveBranch(IInvocationOperation call)
    {
        for (var op = call.Parent; op is not null; op = op.Parent)
        {
            if (op is IConditionalOperation) return true;
        }

        return false;
    }

    private static bool TryMemoized(
        IOperation body,
        AnalysisState state,
        out ComplexityExpression states)
    {
        states = Cx.One;
        var target = Walk(body)
            .OfType<ISimpleAssignmentOperation>()
            .Where(IsMemoAssignment)
            .Select(a => SizeResolver.Unwrap(a.Target))
            .Select(MemoTarget)
            .FirstOrDefault(s => s is not null);
        if (target is null) return false;
        states = state.SizeOf(target);
        return true;
    }

    private static bool IsMemoAssignment(ISimpleAssignmentOperation assign)
    {
        if (assign.Target.Type?.SpecialType == SpecialType.System_Boolean)
            return false;
        var target = SizeResolver.Unwrap(assign.Target);
        if (target is IArrayElementReferenceOperation
            {
                Indices.Length: >= 2
            })
        {
            return true;
        }

        return target is IPropertyReferenceOperation
        {
            Property.IsIndexer: true,
            Arguments.Length: >= 2
        };
    }

    private static ISymbol? MemoTarget(IOperation? target) =>
        target switch
        {
            IArrayElementReferenceOperation e =>
                SizeResolver.TargetSymbol(e.ArrayReference),
            IPropertyReferenceOperation p when p.Property.IsIndexer =>
                SizeResolver.TargetSymbol(p.Instance),
            _ => null,
        };

    private static bool IsSubsetGeneration(
        IOperation body, IInvocationOperation[] calls)
    {
        if (calls.Length != 2) return false;
        if (!calls.All(c => KindOf(c) == ArgKind.PlusOne)) return false;
        return HasMaterializedCopy(body);
    }

    private static bool TryPermutationOrCombination(
        IOperation body,
        IInvocationOperation[] calls,
        AnalysisState state,
        out ComplexityExpression time,
        out ComplexityExpression space)
    {
        time = Cx.One;
        space = Cx.One;
        if (calls.Length == 0) return false;
        if (!calls.All(c => KindOf(c) is ArgKind.PlusOne or ArgKind.Other))
            return false;
        if (!HasLoopAround(calls[0]) || !HasMaterializedCopy(body))
            return false;

        var copied = CopiedSize(body, state);
        var n = Cx.Var("n");
        if (copied is VariableExpression { Name: not "n" } k)
        {
            time = Cx.Mul(k, Cx.Binomial(n, k));
            space = time;
            return true;
        }

        time = Cx.Mul(n, Cx.Factorial(n));
        space = time;
        return true;
    }

    private static bool TryGraphWalk(
        IOperation body,
        AnalysisState state,
        out ComplexityExpression time,
        out ComplexityExpression space)
    {
        time = Cx.One;
        space = Cx.One;
        var loop = Walk(body).OfType<IForEachLoopOperation>().FirstOrDefault();
        if (loop is null) return false;
        if (!Walk(body).OfType<ISimpleAssignmentOperation>().Any(IsVisitedWrite))
            return false;

        var vertices = VerticesOf(loop, state);
        var edges = SizeResolver.Resolve(loop.Collection, state);
        time = Cx.Mul(vertices, edges);
        space = vertices;
        return true;
    }

    private static ComplexityExpression VerticesOf(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var owner = SizeResolver.TargetSymbol(loop.Collection);
        return owner is null ? Cx.Var("n") : state.SizeOf(owner);
    }

    private static bool IsVisitedWrite(ISimpleAssignmentOperation assign)
    {
        var target = SizeResolver.Unwrap(assign.Target);
        return target is IArrayElementReferenceOperation
            && target.Type?.SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsBranchingDecrease(
        IMethodSymbol method, IInvocationOperation[] calls)
    {
        if (calls.Length < 2) return false;
        if (calls.All(IsExclusiveBranch)) return false;
        return calls.All(c => ArgumentKind(method, c) is
            ArgKind.MinusOne or ArgKind.MinusTwo);
    }

    private static bool HasMaterializedCopy(IOperation body) =>
        Walk(body).Any(op =>
            op is IInvocationOperation
            {
                TargetMethod.Name: "Clone" or "ToArray" or "ToList"
            }
            || op is IObjectCreationOperation create
                && create.Arguments.Length == 1
                && DimensionInferrer.IsCollection(
                    create.Arguments[0].Value.Type));

    private static ComplexityExpression? CopiedSize(
        IOperation body, AnalysisState state)
    {
        foreach (var op in Walk(body))
        {
            if (op is IInvocationOperation
                {
                    TargetMethod.Name: "Clone"
                } clone)
            {
                return SizeResolver.Resolve(clone.Instance ?? clone, state);
            }

            if (op is IArrayCreationOperation array)
                return SizeResolver.Resolve(array, state);
        }

        return null;
    }

    private static bool HasLoopAround(IOperation operation)
    {
        for (var op = operation.Parent; op is not null; op = op.Parent)
        {
            if (op is IForLoopOperation or IForEachLoopOperation)
                return true;
        }

        return false;
    }

    private static ArgKind KindOf(IInvocationOperation call)
    {
        foreach (var arg in call.Arguments)
        {
            var kind = ClassifyArg(SizeResolver.Unwrap(arg.Value));
            if (kind != ArgKind.Other) return kind;
        }

        return ArgKind.Other;
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

        return ClassifyArg(SizeResolver.Unwrap(call.Arguments[index].Value));
    }

    private static ArgKind ClassifyArg(IOperation? arg) =>
        arg switch
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
                OperatorKind: BinaryOperatorKind.Subtract,
                RightOperand: ILiteralOperation
                {
                    ConstantValue.Value: 2
                }
            } => ArgKind.MinusTwo,
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                RightOperand: ILiteralOperation
                {
                    ConstantValue.Value: 1
                }
            } => ArgKind.PlusOne,
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

    private static bool IsMidSplit(IOperation operation) =>
        SizeResolver.Unwrap(operation) is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Divide
        } divide
        && SizeResolver.Unwrap(divide.RightOperand) is ILiteralOperation
        {
            ConstantValue.Value: 2
        };

    private static ComposedCost Linear(
        IMethodSymbol method, AnalysisState state)
    {
        var n = Cx.Var("n");
        return Solved(
            method, state, n, n, $"{method.Name}(n-1) linear recurrence");
    }

    private static ComposedCost DivideAndConquer(
        IMethodSymbol method, AnalysisState state)
    {
        var nLogN = Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("n")));
        return Solved(
            method, state, nLogN, Cx.Var("n"),
            $"{method.Name}: T(n)=2T(n/2)+O(n)");
    }

    private static ComposedCost Solved(
        IMethodSymbol method,
        AnalysisState state,
        ComplexityExpression time,
        ComplexityExpression space,
        string label)
    {
        state.Note(
            AnalysisConfidence.Medium,
            "Recurrence classified as " + label
            + "; a different control-flow shape may not match.");
        return new ComposedCost
        {
            Time = time,
            Space = space,
            Confidence = AnalysisConfidence.Medium,
            Evidence = ComplexityEvidence.Leaf(
                "recursion",
                $"{method.Name}: {label}",
                time,
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

    private enum ArgKind { MinusOne, MinusTwo, PlusOne, Half, Other }
}
