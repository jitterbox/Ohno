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
/// accepts a local or parameter of an integral or
/// <c>BigInteger</c> type; <c>IQueryable</c> / EF execution is
/// delegated to a provider and is never given a polynomial;
/// <c>await foreach</c> stays opaque even when a collection bound
/// exists, while a lone <c>await</c> beside a resolved loop is
/// annotated rather than wiping the bound.
/// </remarks>
internal static class PatternRecognizer
{
    public static IReadOnlyList<RecognizedPattern> Recognize(
        IMethodSymbol method,
        IOperation? body,
        AnalysisState? state = null)
    {
        if (body is null) return [];
        var hits = new List<RecognizedPattern>();
        var facts = new Dictionary<IOperation, LoopFacts>();
        Collect(method, body, inLoop: false, hits, facts, state, depth: 0);
        return hits.DistinctBy(h => h.Id).ToArray();
    }

    private static void Collect(
        IMethodSymbol method,
        IOperation operation,
        bool inLoop,
        List<RecognizedPattern> hits,
        Dictionary<IOperation, LoopFacts> facts,
        AnalysisState? state,
        int depth)
    {
        if (depth >= AnalysisState.MaxOperationDepth) return;
        var hit = Match(method, operation, inLoop, facts, state);
        if (hit is not null) hits.Add(hit);
        var nested = inLoop || operation is IForLoopOperation
            or IForEachLoopOperation or IWhileLoopOperation;
        foreach (var child in operation.ChildOperations)
            Collect(method, child, nested, hits, facts, state, depth + 1);
    }

    /// <summary>
    /// Everything the loop detectors ask about a body, gathered in one
    /// pass and cached.
    /// </summary>
    /// <remarks>
    /// <c>UnboundedWorklist</c> alone used to walk the same body up to
    /// seven times (grow, shrink, successor-grow, visit-write, and two
    /// more inside the net-decrease count), and the outer traversal
    /// reaches every nested loop, so the walks compounded with nesting.
    /// </remarks>
    private sealed record LoopFacts
    {
        public HashSet<ISymbol> Grows { get; } =
            new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> Shrinks { get; } =
            new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> SuccessorGrows { get; } =
            new(SymbolEqualityComparer.Default);

        public bool HasVisitWrite { get; set; }

        public bool HasMultiplyOrDivide { get; set; }

        public bool WalksNext { get; set; }

        public int GrowCount { get; set; }

        public int ShrinkCount { get; set; }
    }

    private static LoopFacts FactsOf(
        IOperation body, Dictionary<IOperation, LoopFacts> cache)
    {
        if (cache.TryGetValue(body, out var cached)) return cached;

        var facts = new LoopFacts();
        foreach (var op in OperationTree.SelfAndDescendants(body))
        {
            switch (op)
            {
                case IInvocationOperation call:
                    Record(call, facts);
                    break;
                case IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Multiply
                        or BinaryOperatorKind.Divide
                }:
                    facts.HasMultiplyOrDivide = true;
                    break;
            }

            if (op is ISimpleAssignmentOperation assign
                && SizeResolver.Unwrap(assign.Value)
                    is IFieldReferenceOperation or IPropertyReferenceOperation)
            {
                facts.WalksNext = true;
            }

            if (WrittenTarget(op) is IArrayElementReferenceOperation)
                facts.HasVisitWrite = true;
        }

        cache[body] = facts;
        return facts;
    }

    private static void Record(IInvocationOperation call, LoopFacts facts)
    {
        var name = call.TargetMethod.Name;
        var grows = name is "Enqueue" or "Push" or "Add";
        var shrinks = name is "Dequeue" or "Pop"
            or "TryDequeue" or "TryPop";
        if (!grows && !shrinks) return;

        // The net-decrease check counts every grow and shrink in the
        // body, regardless of which collection it targets.
        if (grows) facts.GrowCount++;
        if (shrinks) facts.ShrinkCount++;

        var target = SizeResolver.TargetSymbol(call.Instance);
        if (target is null) return;
        if (shrinks) facts.Shrinks.Add(target);
        if (!grows) return;

        facts.Grows.Add(target);
        if (name is "Enqueue" or "Push"
            && call.Arguments.Length > 0
            && IsNextField(call.Arguments[0].Value))
        {
            facts.SuccessorGrows.Add(target);
        }
    }

    private static RecognizedPattern? Match(
        IMethodSymbol method,
        IOperation operation,
        bool inLoop,
        Dictionary<IOperation, LoopFacts> facts,
        AnalysisState? state) =>
        Dynamic(operation)
        ?? Reflection(operation)
        ?? Interface(operation)
        ?? Delegate(operation)
        ?? OpaqueCall(operation, state)
        ?? Queryable(operation)
        ?? Await(operation)
        ?? DeferredLinq(operation)
        ?? Lock(operation)
        ?? Yield(operation)
        ?? StringConcat(operation, inLoop)
        ?? Collatz(operation, facts)
        ?? NullWalk(operation, facts)
        ?? Countdown(operation)
        ?? Cache(operation)
        ?? UnboundedWorklist(operation, facts)
        ?? BranchingRecursion(method, operation);

    private static RecognizedPattern? Dynamic(IOperation operation) =>
        operation is IDynamicInvocationOperation
            or IDynamicMemberReferenceOperation
            ? Unknown(
                "dynamic-dispatch",
                "Dynamic dispatch",
                "the invocation target is selected by the runtime binder",
                operation)
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
            "the target method is selected by name at runtime",
            operation);
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
            "the concrete implementation is not fixed at this call site",
            operation);
    }

    private static RecognizedPattern? Delegate(IOperation operation)
    {
        if (operation is not IInvocationOperation call) return null;
        if (call.TargetMethod.MethodKind != MethodKind.DelegateInvoke)
            return null;
        return Unknown(
            "delegate-invoke",
            "Delegate invocation",
            "the delegate body and invocation-list length are not known here",
            operation);
    }

    private static RecognizedPattern? OpaqueCall(
        IOperation operation, AnalysisState? state)
    {
        if (operation is not IInvocationOperation call) return null;
        var type = SymbolKeys.TypeName(call.TargetMethod.ContainingType);
        if (type == "System.Text.RegularExpressions.Regex"
            && state is not null
            && RegexFacts.IsLinear(call, state))
        {
            // The non-backtracking engine never revisits a character,
            // so this one has a real bound and must not be wiped.
            return Annotate(
                "regex-linear",
                "Non-backtracking regular expression",
                "the engine scans the input once, so the match is "
                + "linear in the subject rather than pattern-dependent",
                operation);
        }

        return type switch
        {
            "System.Text.RegularExpressions.Regex" =>
                Unknown("regex", "Regular expression",
                    "matching cost depends on the pattern and can backtrack",
                    operation),
            "System.IO.Stream" =>
                Unknown("stream-io", "Stream I/O",
                    "the concrete stream may be memory, file, or network",
                    operation),
            "System.Threading.Tasks.Parallel" =>
                Unknown("parallel-loop", "Parallel loop",
                    "elapsed time depends on scheduling and the callback",
                    operation),
            "System.Linq.Expressions.LambdaExpression" or
            "System.Linq.Expressions.Expression`1" =>
                Unknown("expression-compile", "Compiled expression tree",
                    "the compiled body is data, not a fixed method",
                    operation),
            "System.Threading.Thread" =>
                Unknown("thread-block", "Thread blocking",
                    "wait time is controlled by the runtime and other threads",
                    operation),
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
            "IQueryable / EF provider",
            "execution is delegated to a query provider "
            + "(EF, LINQ to SQL, or another IQueryable engine)",
            operation);
    }

    private static RecognizedPattern? Await(IOperation operation)
    {
        if (operation is IAwaitOperation)
        {
            return Unknown(
                "await-opaque",
                "Awaited work",
                "the awaited operation's cost is not the local continuation",
                operation);
        }

        if (operation is IForEachLoopOperation loop
            && HasAsyncEnumerator(loop.Collection.Type))
        {
            return Unknown(
                "await-foreach",
                "Awaited sequence",
                "the async sequence's MoveNextAsync cost is not local",
                operation);
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
            "Deferred LINQ (in-memory)",
            "System.Linq.Enumerable builds a query in constant time; "
            + "cost is paid when enumerated. This is not EF / IQueryable",
            operation);
    }

    private static RecognizedPattern? Lock(IOperation operation) =>
        operation is ILockOperation
            ? Annotate(
                "lock-wait",
                "Lock",
                "local work is constant but wait time depends on other threads",
                operation)
            : null;

    private static RecognizedPattern? Yield(IOperation operation) =>
        operation.Kind == OperationKind.YieldReturn
            ? Annotate(
                "iterator-yield",
                "Iterator",
                "cost depends on how many elements the caller consumes",
                operation)
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
                "each concatenation copies a growing string",
                operation)
            : null;
    }

    private static RecognizedPattern? Collatz(
        IOperation operation,
        Dictionary<IOperation, LoopFacts> facts)
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

        if (!FactsOf(loop.Body, facts).HasMultiplyOrDivide) return null;
        return Unknown(
            "unproven-loop",
            "Unproven loop bound",
            "the loop variable is not a proven decreasing size metric",
            operation);
    }

    private static RecognizedPattern? NullWalk(
        IOperation operation,
        Dictionary<IOperation, LoopFacts> facts)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        var cond = SizeResolver.Unwrap(loop.Condition);
        var pattern = cond is IIsPatternOperation
            || cond is IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.ConditionalAnd
                    or BinaryOperatorKind.ConditionalOr
            };
        if (!pattern || !FactsOf(loop.Body, facts).WalksNext) return null;
        return Annotate(
            "null-terminated-walk",
            "Null-terminated walk",
            "the bound assumes a finite acyclic chain",
            operation);
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
                "the bound is the numeric value, not its encoded size",
                operation)
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
            + "a cache hit is constant time",
            RoslynSpans.Of(operation));
    }

    private static RecognizedPattern? UnboundedWorklist(
        IOperation operation,
        Dictionary<IOperation, LoopFacts> cache)
    {
        if (operation is not IWhileLoopOperation loop) return null;
        if (!IsCountCondition(loop.Condition, out var work))
            return null;
        var facts = FactsOf(loop.Body, cache);
        if (!facts.Grows.Contains(work) || !facts.Shrinks.Contains(work))
            return null;
        if (facts.HasVisitWrite) return null;
        if (facts.SuccessorGrows.Contains(work)) return null;
        if (IsNetDecrease(loop, facts)) return null;
        return Unknown(
            "unbounded-worklist",
            "Unbounded worklist",
            "the queue is refilled without a visit mark and may not halt",
            operation);
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

    private static bool IsNetDecrease(
        IWhileLoopOperation loop, LoopFacts facts)
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

        return facts.ShrinkCount > facts.GrowCount;
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
            + "worst case is exponential",
            RoslynSpans.Of(operation));
    }

    private static RecognizedPattern Unknown(
        string id, string label, string reason,
        IOperation? operation = null) =>
        new(id, label, reason, PatternEffect.Unknown, "",
            RoslynSpans.Of(operation));

    private static RecognizedPattern Annotate(
        string id, string label, string reason,
        IOperation? operation = null) =>
        new(id, label, reason, PatternEffect.Annotate, "",
            RoslynSpans.Of(operation));

    private static int CountRecursive(
        IMethodSymbol method, IOperation body) =>
        OperationTree.SelfAndDescendants(body).OfType<IInvocationOperation>()
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
        var type = left switch
        {
            ILocalReferenceOperation local => local.Type,
            IParameterReferenceOperation p => p.Type,
            _ => null,
        };
        if (type is null) return false;
        return type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            || SymbolKeys.TypeName(type) == "System.Numerics.BigInteger";
    }


    private static bool IsLibraryInterface(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.StartsWith("System.Collections", StringComparison.Ordinal)
            || ns.StartsWith("System.Linq", StringComparison.Ordinal);
    }


}
