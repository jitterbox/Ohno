using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Source-level hazard and idiom detectors. These do not solve recurrences;
/// they name a pattern and say whether a bound is conclusive.
/// </summary>
/// <remarks>
/// Matching uses <see cref="IOperation"/>, not tokens, so <c>lock</c>
/// (<see href="https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock">lock statement</see>),
/// <c>yield return</c>
/// (<see href="https://learn.microsoft.com/dotnet/csharp/language-reference/statements/yield">yield</see>),
/// and <c>dynamic</c> invocations are kind checks.
/// Edge cases: Collatz-like loops only fire when the condition is
/// equality and the body contains <c>*</c> or <c>/</c>; countdown
/// requires a local (not a parameter) of an integral or
/// <c>BigInteger</c> type; <c>IQueryable</c> execution is delegated
/// to a provider and is never given a polynomial.
/// </remarks>
internal static class PatternRecognizer
{
    public static IReadOnlyList<RecognizedPattern> Recognize(
        IMethodSymbol method, IOperation? body)
    {
        if (body is null) return [];
        var hits = new List<RecognizedPattern>();
        Walk(method, body, inLoop: false, hits);
        return hits.DistinctBy(h => h.Id).ToArray();
    }

    private static void Walk(
        IMethodSymbol method,
        IOperation operation,
        bool inLoop,
        List<RecognizedPattern> hits)
    {
        var hit = Match(method, operation, inLoop);
        if (hit is not null) hits.Add(hit);
        var nested = inLoop || operation is IForLoopOperation
            or IForEachLoopOperation or IWhileLoopOperation;
        foreach (var child in operation.ChildOperations)
            Walk(method, child, nested, hits);
    }

    private static RecognizedPattern? Match(
        IMethodSymbol method, IOperation operation, bool inLoop) =>
        Dynamic(operation)
        ?? Reflection(operation)
        ?? Interface(operation)
        ?? Delegate(operation)
        ?? OpaqueCall(operation)
        ?? Queryable(operation)
        ?? Await(operation)
        ?? DeferredLinq(operation)
        ?? Lock(operation)
        ?? Yield(operation)
        ?? StringConcat(operation, inLoop)
        ?? Collatz(operation)
        ?? NullWalk(operation)
        ?? Countdown(operation)
        ?? Cache(operation)
        ?? UnboundedWorklist(operation)
        ?? BranchingRecursion(method, operation);

    private static RecognizedPattern? Dynamic(IOperation operation) =>
        operation is IDynamicInvocationOperation
            or IDynamicMemberReferenceOperation
            ? Unknown(
                "dynamic-dispatch",
                "Dynamic dispatch",
                "the invocation target is selected by the runtime binder")
            : null;

    private static RecognizedPattern? Reflection(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        var type = SymbolKeys.TypeName(call.TargetMethod.ContainingType);
        if (type is not ("System.Reflection.MethodInfo"
            or "System.Reflection.MethodBase"))
        {
            return null;
        }

        return Unknown(
            "reflection-dispatch",
            "Reflection dispatch",
            "the target method is selected by name at runtime");
    }

    private static RecognizedPattern? Interface(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        var method = call.TargetMethod;
        if (IsLibraryInterface(method.ContainingType)) return null;
        if (method.ContainingType.TypeKind != TypeKind.Interface)
            return null;

        return Unknown(
            "interface-dispatch",
            "Interface or abstract dispatch",
            "the concrete implementation is not fixed at this call site");
    }

    private static RecognizedPattern? Delegate(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        if (call.TargetMethod.MethodKind != MethodKind.DelegateInvoke)
            return null;
        return Unknown(
            "delegate-invoke",
            "Delegate invocation",
            "the delegate body and invocation-list length are not known here");
    }

    private static RecognizedPattern? OpaqueCall(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        var type = SymbolKeys.TypeName(call.TargetMethod.ContainingType);
        return type switch
        {
            "System.Text.RegularExpressions.Regex" =>
                Unknown("regex", "Regular expression",
                    "matching cost depends on the pattern and can backtrack"),
            "System.IO.Stream" =>
                Unknown("stream-io", "Stream I/O",
                    "the concrete stream may be memory, file, or network"),
            "System.Threading.Tasks.Parallel" =>
                Unknown("parallel-loop", "Parallel loop",
                    "elapsed time depends on scheduling and the callback"),
            "System.Linq.Expressions.LambdaExpression" or
            "System.Linq.Expressions.Expression`1" =>
                Unknown("expression-compile", "Compiled expression tree",
                    "the compiled body is data, not a fixed method"),
            "System.Threading.Thread" =>
                Unknown("thread-block", "Thread blocking",
                    "wait time is controlled by the runtime and other threads"),
            _ => null,
        };
    }

    private static RecognizedPattern? Queryable(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        var source = call.Instance ?? call.Arguments.FirstOrDefault()?.Value;
        if (!SymbolKeys.IsQueryable(source?.Type)
            && !SymbolKeys.IsQueryable(call.TargetMethod.ContainingType))
        {
            return null;
        }

        return Unknown(
            "queryable",
            "IQueryable provider",
            "execution is delegated to a runtime query provider");
    }

    private static RecognizedPattern? Await(IOperation operation)
    {
        if (operation is IAwaitOperation)
        {
            return Unknown(
                "await-opaque",
                "Awaited work",
                "the awaited operation's cost is not the local continuation");
        }

        if (operation is IForEachLoopOperation loop
            && HasAsyncEnumerator(loop.Collection.Type))
        {
            return Unknown(
                "await-opaque",
                "Awaited work",
                "the async sequence's MoveNextAsync cost is not local");
        }

        return null;
    }

    private static RecognizedPattern? DeferredLinq(IOperation operation)
    {
        if (operation is not IReturnOperation ret) return null;
        if (ret.ReturnedValue is not IInvocationOperation call)
            return null;
        var type = SymbolKeys.TypeName(call.TargetMethod.ContainingType);
        if (type is not "System.Linq.Enumerable") return null;
        return Annotate(
            "deferred-linq",
            "Deferred LINQ",
            "the query is built in constant time; "
            + "cost is paid when enumerated");
    }

    private static RecognizedPattern? Lock(IOperation operation) =>
        operation is ILockOperation
            ? Annotate(
                "lock-wait",
                "Lock",
                "local work is constant but wait time depends on other threads")
            : null;

    private static RecognizedPattern? Yield(IOperation operation) =>
        operation.Kind == OperationKind.YieldReturn
            ? Annotate(
                "iterator-yield",
                "Iterator",
                "cost depends on how many elements the caller consumes")
            : null;

    private static RecognizedPattern? StringConcat(
        IOperation operation, bool inLoop)
    {
        if (!inLoop) return null;
        var concat = operation is ICompoundAssignmentOperation
        {
            OperatorKind: BinaryOperatorKind.Add
        } c && IsString(c.Target.Type);
        concat = concat || operation is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add
        } b && IsString(b.Type);
        return concat
            ? Annotate(
                "string-concat-loop",
                "Repeated string concatenation",
                "each concatenation copies a growing string")
            : null;
    }

    private static RecognizedPattern? Collatz(IOperation operation)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        var cond = SizeResolver.Unwrap(loop.Condition);
        if (cond is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.NotEquals
                    or BinaryOperatorKind.Equals
            })
        {
            return null;
        }

        if (!AssignmentGrows(loop.Body)) return null;
        return Unknown(
            "unproven-loop",
            "Unproven loop bound",
            "the loop variable is not a proven decreasing size metric");
    }

    private static RecognizedPattern? NullWalk(IOperation operation)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        var cond = SizeResolver.Unwrap(loop.Condition);
        var pattern = cond is IIsPatternOperation
            || cond is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd
                    or BinaryOperatorKind.ConditionalOr
            };
        return pattern
            ? Annotate(
                "null-terminated-walk",
                "Null-terminated walk",
                "the bound assumes a finite acyclic chain")
            : null;
    }

    private static RecognizedPattern? Countdown(IOperation operation)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        var cond = SizeResolver.Unwrap(loop.Condition);
        if (cond is not IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.GreaterThan
                    or BinaryOperatorKind.GreaterThanOrEqual
            })
        {
            return null;
        }

        return IsNumericCountdown(loop)
            ? Annotate(
                "numeric-countdown",
                "Numeric countdown",
                "the bound is the numeric value, not its encoded size")
            : null;
    }

    private static RecognizedPattern? Cache(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        if (call.TargetMethod.Name != "TryGetValue") return null;
        var type = SymbolKeys.TypeName(call.Instance?.Type);
        if (type is not "System.Collections.Generic.Dictionary`2")
            return null;
        return new RecognizedPattern(
            "cache-history",
            "Cache-dependent work",
            "a hit is constant time; a miss repeats the full computation",
            PatternEffect.Range,
            "Worst case matches the uncached work; "
            + "a cache hit is constant time");
    }

    private static RecognizedPattern? UnboundedWorklist(
        IOperation operation)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        if (!IsCountCondition(loop.Condition, out var work))
            return null;
        if (!HasGrow(loop.Body, work) || !HasShrink(loop.Body, work))
            return null;
        if (HasVisitWrite(loop.Body)) return null;
        if (HasSuccessorGrow(loop.Body, work)) return null;
        if (IsNetDecrease(loop)) return null;
        return Unknown(
            "unbounded-worklist",
            "Unbounded worklist",
            "the queue is refilled without a visit mark and may not halt");
    }

    private static bool IsCountCondition(
        IOperation? condition, out ISymbol work)
    {
        work = null!;
        if (SizeResolver.Unwrap(condition) is not IBinaryOperation binary)
            return false;
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

    private static bool HasGrow(IOperation body, ISymbol work) =>
        WalkAll(body).OfType<IInvocationOperation>().Any(c =>
            c.TargetMethod.Name is "Enqueue" or "Push" or "Add"
            && SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(c.Instance), work));

    private static bool HasShrink(IOperation body, ISymbol work) =>
        WalkAll(body).OfType<IInvocationOperation>().Any(c =>
            c.TargetMethod.Name is "Dequeue" or "Pop"
                or "TryDequeue" or "TryPop"
            && SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(c.Instance), work));

    private static bool HasSuccessorGrow(
        IOperation body, ISymbol work) =>
        WalkAll(body).OfType<IInvocationOperation>().Any(c =>
            c.TargetMethod.Name is "Enqueue" or "Push"
            && c.Arguments.Length > 0
            && IsNextField(c.Arguments[0].Value)
            && SymbolEqualityComparer.Default.Equals(
                SizeResolver.TargetSymbol(c.Instance), work));

    private static bool IsNextField(IOperation? value)
    {
        var op = SizeResolver.Unwrap(value);
        var name = op switch
        {
            IFieldReferenceOperation f => f.Field.Name,
            IPropertyReferenceOperation p => p.Property.Name,
            _ => null,
        };
        return name is not null
            && name.Equals("next", StringComparison.OrdinalIgnoreCase);
    }

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

        var shrinks = WalkAll(loop.Body)
            .OfType<IInvocationOperation>()
            .Count(c => c.TargetMethod.Name is "Dequeue" or "Pop"
                or "TryDequeue" or "TryPop");
        var grows = WalkAll(loop.Body)
            .OfType<IInvocationOperation>()
            .Count(c => c.TargetMethod.Name is "Enqueue" or "Push"
                or "Add");
        return shrinks > grows;
    }

    private static bool HasVisitWrite(IOperation body) =>
        WalkAll(body).Any(op =>
            WrittenTarget(op) is IArrayElementReferenceOperation);

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

    private static RecognizedPattern? BranchingRecursion(
        IMethodSymbol method, IOperation operation)
    {
        if (operation is not IConditionalOperation cond) return null;
        var whenTrue = CountRecursive(method, cond.WhenTrue);
        var whenFalse = cond.WhenFalse is null
            ? 0
            : CountRecursive(method, cond.WhenFalse);
        if (whenTrue == whenFalse || (whenTrue < 1 && whenFalse < 1))
            return null;
        return new RecognizedPattern(
            "data-dependent-recursion",
            "Data-dependent recursion",
            "the number of recursive calls depends on the input values",
            PatternEffect.Range,
            "Best case is linear in remaining elements; "
            + "worst case is exponential");
    }

    private static RecognizedPattern Unknown(
        string id, string label, string reason) =>
        new(id, label, reason, PatternEffect.Unknown);

    private static RecognizedPattern Annotate(
        string id, string label, string reason) =>
        new(id, label, reason, PatternEffect.Annotate);

    private static int CountRecursive(
        IMethodSymbol method, IOperation body) =>
        WalkAll(body).OfType<IInvocationOperation>()
            .Count(c => SymbolEqualityComparer.Default.Equals(
                c.TargetMethod.OriginalDefinition,
                method.OriginalDefinition));

    private static bool IsString(ITypeSymbol? type) =>
        type?.SpecialType == SpecialType.System_String;

    private static bool HasAsyncEnumerator(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type.GetMembers("GetAsyncEnumerator").Length > 0)
            return true;
        return type.AllInterfaces.Any(i =>
            SymbolKeys.TypeName(i)
                == "System.Collections.Generic.IAsyncEnumerable`1");
    }

    private static bool IsNumericCountdown(IWhileLoopOperation loop)
    {
        var cond = SizeResolver.Unwrap(loop.Condition);
        if (cond is not IBinaryOperation binary) return false;
        var left = SizeResolver.Unwrap(binary.LeftOperand);
        if (left is not ILocalReferenceOperation local) return false;
        var type = local.Type;
        if (type is null) return false;
        return type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            || SymbolKeys.TypeName(type) == "System.Numerics.BigInteger";
    }

    private static bool AssignmentGrows(IOperation body) =>
        WalkAll(body).OfType<IBinaryOperation>()
            .Any(b => b.OperatorKind is BinaryOperatorKind.Multiply
                or BinaryOperatorKind.Divide);

    private static bool IsLibraryInterface(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.StartsWith("System.Collections", StringComparison.Ordinal)
            || ns.StartsWith("System.Linq", StringComparison.Ordinal);
    }

    private static IEnumerable<IOperation> WalkAll(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var nested in WalkAll(child))
                yield return nested;
        }
    }
}
