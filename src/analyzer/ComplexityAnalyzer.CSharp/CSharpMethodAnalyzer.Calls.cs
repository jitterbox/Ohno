using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

public sealed partial class CSharpMethodAnalyzer
{
    /// <summary>
    /// Resolves a call through the catalog, a walked user body, or an
    /// opaque/unknown placeholder.
    /// </summary>
    /// <remarks>
    /// Interface and abstract dispatch have no fixed body
    /// (<see href="https://learn.microsoft.com/dotnet/csharp/fundamentals/object-oriented/interfaces">interfaces</see>).
    /// User-defined operators are walked when syntax exists
    /// (<see href="https://learn.microsoft.com/dotnet/csharp/language-reference/operators/operator-overloading">operator overloading</see>).
    /// <c>Add</c> of a newly allocated collection inside a loop is
    /// treated as retained (iterations × size), not peak-only.
    /// </remarks>
    private ComposedCost AnalyzeInvocation(
        IInvocationOperation call, AnalysisState state)
    {
        var method = call.TargetMethod.OriginalDefinition;
        if (IsDynamicOrUnresolvable(call, method))
            return UnknownCall(method.Name, RoslynSpans.Of(call),
                "dynamic or unresolved dispatch");

        var key = SymbolKeys.ForMethod(method);
        if (key is not null && _catalog.TryGet(key, out var entry))
            return FromCatalog(call, entry, state);

        if (IsVirtualUnknown(method))
            return UnknownCall(method.Name, RoslynSpans.Of(call),
                "virtual or interface dispatch is not statically known");

        if (method.DeclaringSyntaxReferences.Length > 0)
            return AnalyzeUserCall(call, method, state);

        if (IsOpaqueSystem(method))
            return UnknownCall(method.Name, RoslynSpans.Of(call),
                "this API's cost is not a fixed primitive");

        if (IsSystemPrimitive(method))
        {
            var confidence = IsNoCostPrimitive(method)
                ? AnalysisConfidence.High
                : AnalysisConfidence.Medium;
            if (confidence < AnalysisConfidence.High)
            {
                state.Note(
                    AnalysisConfidence.Medium,
                    "A System API was treated as a constant-time primitive "
                    + "without a catalog summary.");
            }

            return ComposedCost.Of(
                Cx.One,
                Cx.One,
                "call",
                method.Name,
                RoslynSpans.Of(call),
                confidence);
        }

        return UnknownCall(
            method.Name,
            RoslynSpans.Of(call),
            $"unresolved call {method.Name}");
    }

    private bool PipelineSorts(IInvocationOperation call)
    {
        var source = call.Instance ?? call.Arguments.FirstOrDefault()?.Value;
        while (SizeResolver.Unwrap(source) is IInvocationOperation inner)
        {
            var key = SymbolKeys.ForMethod(inner.TargetMethod.OriginalDefinition);
            if (key is not null
                && _catalog.TryGet(key, out var innerEntry)
                && innerEntry.Sorts)
            {
                return true;
            }

            source = inner.Instance
                ?? inner.Arguments.FirstOrDefault()?.Value;
        }

        return false;
    }

    private static bool IsNoCostPrimitive(IMethodSymbol method) =>
        method.Name is "KeepAlive" or "GetHashCode" or "Equals"
            or "ToString"
        || SymbolKeys.TypeName(method.ContainingType) == "System.GC";

    private static bool IsSystemPrimitive(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
        if (!ns.StartsWith("System", StringComparison.Ordinal)
            && !ns.StartsWith("Microsoft", StringComparison.Ordinal))
        {
            return false;
        }

        return !DimensionInferrer.IsCollection(method.ContainingType);
    }

    private ComposedCost FromCatalog(
        IInvocationOperation call,
        CatalogEntry entry,
        AnalysisState state)
    {
        if (entry.IsQueryable || SourceIsQueryable(call))
            return QueryableCall(call, entry, state);

        if (entry.Deferred && !entry.Materializes && !entry.Sorts)
        {
            return ComposedCost.Unit(
                "linq",
                $"{call.TargetMethod.Name} (deferred)",
                RoslynSpans.Of(call));
        }

        var size = ReceiverSize(call, state);
        size = ApplyHeapBound(call, size, state);
        NoteGrowth(call, size, state);

        var sorts = entry.Sorts || PipelineSorts(call);
        var time = sorts
            ? Cx.Mul(size, Cx.Log(size))
            : entry.Time.Bind(size);
        if (entry.Materializes)
            state.Retained.Add(entry.Space.Bind(size));

        var confidence = entry.Kind is CostKind.Exact
            ? AnalysisConfidence.High
            : AnalysisConfidence.Medium;
        if (confidence < AnalysisConfidence.High)
        {
            state.Note(
                AnalysisConfidence.Medium,
                "A library cost is amortized or expected, "
                + "not a worst-case guarantee.");
        }
        var label = call.TargetMethod.Name;
        if (entry.Kind is CostKind.Expected)
            label += " (expected)";
        if (entry.Kind is CostKind.Amortized)
            label += " (amortized)";

        var cost = ComposedCost.Of(
            ComplexitySimplifier.Simplify(time),
            entry.Space.Bind(size),
            entry.Deferred ? "linq" : "call",
            label,
            RoslynSpans.Of(call),
            confidence);

        var argumentCosts = call.Arguments
            .Select(a => Analyze(a.Value, state))
            .ToArray();
        if (argumentCosts.Length == 0) return cost;
        return CostComposer.Sequential(
            argumentCosts.Append(cost).ToArray(),
            RoslynSpans.Of(call));
    }

    private ComposedCost QueryableCall(
        IInvocationOperation call,
        CatalogEntry entry,
        AnalysisState state)
    {
        var span = RoslynSpans.Of(call);
        state.Warnings.Add(new AnalysisWarning(
            "Query executes database-side; complexity depends on " +
            "provider, schema, and indexes.",
            span));
        var space = entry.Materializes
            ? SizeResolver.Resolve(call.Arguments.FirstOrDefault()?.Value, state)
            : Cx.One;
        return new ComposedCost
        {
            Time = Cx.Unknown("database"),
            Space = space,
            Confidence = AnalysisConfidence.Unknown,
            Evidence = ComplexityEvidence.Leaf(
                "linq",
                call.TargetMethod.Name,
                Cx.Unknown("database"),
                span),
            Warnings = new[] { state.Warnings[^1] },
        };
    }

    private ComposedCost AnalyzeUserCall(
        IInvocationOperation call,
        IMethodSymbol method,
        AnalysisState state)
    {
        var model = call.SemanticModel;
        if (model is null)
            return UnknownCall(method.Name, RoslynSpans.Of(call), "no model");

        var callee = AnalyzeSymbol(method, model, state);
        var args = call.Arguments
            .Select(a => Analyze(a.Value, state))
            .ToArray();
        var combined = args.Length == 0
            ? callee
            : CostComposer.Sequential(
                args.Append(callee).ToArray(), RoslynSpans.Of(call));
        return combined with
        {
            Evidence = new ComplexityEvidence(
                "call",
                method.Name,
                combined.Time,
                RoslynSpans.Of(call),
                new[] { callee.Evidence }),
        };
    }

    private ComposedCost AnalyzeCreation(
        IObjectCreationOperation create, AnalysisState state)
    {
        var args = create.Arguments
            .Select(a => Analyze(a.Value, state))
            .ToArray();
        var cataloged = CatalogCtor(create, state);
        var copy = cataloged ?? CollectionCopy(create, state);
        var ctor = copy ?? ComposedCost.Unit(
            "alloc",
            create.Type?.Name ?? "new",
            RoslynSpans.Of(create));
        return args.Length == 0
            ? ctor
            : CostComposer.Sequential(
                args.Append(ctor).ToArray(), RoslynSpans.Of(create));
    }

    private ComposedCost? CatalogCtor(
        IObjectCreationOperation create, AnalysisState state)
    {
        var ctor = create.Constructor;
        if (ctor is null) return null;
        var key = SymbolKeys.ForMethod(ctor.OriginalDefinition);
        if (key is null || !_catalog.TryGet(key, out var entry))
            return null;
        var size = create.Arguments.Length > 0
            ? SizeResolver.Resolve(create.Arguments[0].Value, state)
            : Cx.One;
        var symbol = SizeResolver.TargetSymbol(create.Parent);
        if (symbol is not null)
        {
            state.Sizes[symbol] = size;
            state.Retained.Add(size);
        }
        else
        {
            state.Retained.Add(size);
        }

        return ComposedCost.Of(
            entry.Time.Bind(size),
            entry.Space.Bind(size),
            "alloc",
            create.Type?.Name ?? ".ctor",
            RoslynSpans.Of(create));
    }

    private static ComposedCost? CollectionCopy(
        IObjectCreationOperation create, AnalysisState state)
    {
        if (create.Arguments.Length != 1) return null;
        if (!DimensionInferrer.IsCollection(create.Type)) return null;
        var source = create.Arguments[0].Value;
        if (!DimensionInferrer.IsCollection(source.Type)) return null;
        var size = SizeResolver.Resolve(source, state);
        state.Retained.Add(size);
        return ComposedCost.Of(
            size, size, "alloc", "copy", RoslynSpans.Of(create));
    }

    private ComplexityExpression ReceiverSize(
        IInvocationOperation call, AnalysisState state)
    {
        if (call.Instance is not null)
            return SizeResolver.Resolve(call.Instance, state);
        if (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0)
            return SizeResolver.Resolve(call.Arguments[0].Value, state);
        if (call.Arguments.Length > 0)
            return SizeResolver.Resolve(call.Arguments[0].Value, state);
        return Cx.Var("n");
    }

    private ComplexityExpression ApplyHeapBound(
        IInvocationOperation call,
        ComplexityExpression size,
        AnalysisState state)
    {
        var symbol = SizeResolver.TargetSymbol(call.Instance);
        if (symbol is not null
            && state.HeapBounds.TryGetValue(symbol, out var bound))
        {
            return bound;
        }

        if (symbol is not null
            && state.Cardinalities.TryGetValue(symbol, out var card))
        {
            return card.Max;
        }

        return size;
    }

    private static bool SourceIsQueryable(IInvocationOperation call)
    {
        var source = call.Instance ?? call.Arguments.FirstOrDefault()?.Value;
        while (source is not null)
        {
            if (SymbolKeys.IsQueryable(source.Type)) return true;
            if (SizeResolver.Unwrap(source) is not IInvocationOperation inner)
                break;
            source = inner.Instance
                ?? inner.Arguments.FirstOrDefault()?.Value;
        }

        return false;
    }

    private void NoteGrowth(
        IInvocationOperation call,
        ComplexityExpression size,
        AnalysisState state)
    {
        var name = call.TargetMethod.Name;
        if (name is not ("Add" or "Enqueue" or "Push" or "set_Item"
            or "Append"))
        {
            return;
        }

        var symbol = SizeResolver.TargetSymbol(call.Instance);
        if (symbol is null) return;

        if (IsPriorityQueue(call.Instance?.Type)
            && !state.HeapBounds.ContainsKey(symbol))
        {
            state.UnboundedHeaps.Add(symbol);
        }

        if (state.HeapBounds.TryGetValue(symbol, out var heapBound))
        {
            RetainGrown(symbol, heapBound, call, state);
            return;
        }

        if (state.Cardinalities.TryGetValue(symbol, out var card))
        {
            RetainGrown(symbol, card.Max, call, state);
            return;
        }

        if (state.FrontierBound is not null
            && name is "Enqueue" or "Add" or "Push")
        {
            state.Retained.Add(state.FrontierBound);
            state.Sizes[symbol] = state.FrontierBound;
            return;
        }

        if (state.CurrentLoopBound is not null)
            RetainGrown(symbol, state.CurrentLoopBound, call, state);
    }

    private void RetainGrown(
        ISymbol symbol,
        ComplexityExpression bound,
        IInvocationOperation call,
        AnalysisState state)
    {
        var added = AddedAllocation(call, state);
        var retained = added is null
            ? bound
            : Cx.Mul(bound, added);
        if (added is not null)
        {
            state.Note(
                AnalysisConfidence.Medium,
                "Retained space assumes the allocation is stored in a "
                + "live collection; a different store may be peak-only.");
        }

        state.Sizes[symbol] = retained;
        state.Retained.Add(retained);
    }

    private static ComplexityExpression? AddedAllocation(
        IInvocationOperation call, AnalysisState state)
    {
        if (call.Arguments.Length == 0) return null;
        var value = SizeResolver.Unwrap(call.Arguments[0].Value);
        return value switch
        {
            IArrayCreationOperation a => SizeResolver.Resolve(a, state),
            IInvocationOperation inv when inv.TargetMethod.Name
                is "Clone" or "ToArray" or "ToList" =>
                SizeResolver.Resolve(inv.Instance ?? inv, state),
            IObjectCreationOperation o
                when DimensionInferrer.IsCollection(o.Type)
                && o.Arguments.Length == 1
                && DimensionInferrer.IsCollection(o.Arguments[0].Value.Type) =>
                SizeResolver.Resolve(o.Arguments[0].Value, state),
            _ => null,
        };
    }

    private static bool IsDynamicOrUnresolvable(
        IInvocationOperation call, IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.DelegateInvoke) return true;
        if (call.Instance is IDynamicMemberReferenceOperation) return true;
        return method.ContainingType?.TypeKind == TypeKind.Error;
    }

    private static bool IsVirtualUnknown(IMethodSymbol method)
    {
        if (method.ContainingType.TypeKind == TypeKind.Interface)
            return true;
        if (method.IsAbstract) return true;
        return method.IsVirtual && method.ContainingType.IsAbstract;
    }

    private static bool IsOpaqueSystem(IMethodSymbol method)
    {
        var type = SymbolKeys.TypeName(method.ContainingType);
        return type is "System.Reflection.MethodInfo"
            or "System.Reflection.MethodBase"
            or "System.Text.RegularExpressions.Regex"
            or "System.IO.Stream"
            or "System.Threading.Tasks.Parallel"
            or "System.Threading.Thread"
            or "System.Linq.Expressions.LambdaExpression"
            or "System.Linq.Expressions.Expression`1"
            or "System.Linq.Expressions.Expression";
    }
}
