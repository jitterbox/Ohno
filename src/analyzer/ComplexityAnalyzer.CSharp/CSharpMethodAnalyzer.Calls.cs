using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

public sealed partial class CSharpMethodAnalyzer
{
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

        if (IsVirtualUnknown(method, state))
            return UnknownCall(method.Name, RoslynSpans.Of(call),
                "virtual or interface dispatch is not statically known");

        if (method.DeclaringSyntaxReferences.Length > 0)
            return AnalyzeUserCall(call, method, state);

        if (IsSystemPrimitive(method))
        {
            return ComposedCost.Of(
                Cx.One,
                Cx.One,
                "call",
                method.Name,
                RoslynSpans.Of(call),
                AnalysisConfidence.Medium);
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
        var ctor = ComposedCost.Unit(
            "alloc",
            create.Type?.Name ?? "new",
            RoslynSpans.Of(create));
        return args.Length == 0
            ? ctor
            : CostComposer.Sequential(
                args.Append(ctor).ToArray(), RoslynSpans.Of(create));
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

        if (call.TargetMethod.Name == "Enqueue"
            && IsPriorityQueue(call.Instance?.Type)
            && state.CurrentLoopBound is not null)
        {
            return state.CurrentLoopBound;
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
        if (name is not ("Add" or "Enqueue" or "Push")) return;
        var symbol = SizeResolver.TargetSymbol(call.Instance);
        if (symbol is null) return;

        if (name == "Enqueue" && IsPriorityQueue(call.Instance?.Type))
        {
            if (state.HeapBounds.TryGetValue(symbol, out var bound))
            {
                state.Retained.Add(bound);
                state.Sizes[symbol] = bound;
                return;
            }

            var grown = state.CurrentLoopBound ?? size;
            state.UnboundedHeaps.Add(symbol);
            state.Retained.Add(grown);
            state.Sizes[symbol] = grown;
            return;
        }

        if (state.CurrentLoopBound is not null)
        {
            state.Sizes[symbol] = state.CurrentLoopBound;
            state.Retained.Add(state.CurrentLoopBound);
        }
    }

    private static bool IsDynamicOrUnresolvable(
        IInvocationOperation call, IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.DelegateInvoke) return true;
        if (call.Instance is IDynamicMemberReferenceOperation) return true;
        return method.ContainingType?.TypeKind == TypeKind.Error;
    }

    private static bool IsVirtualUnknown(
        IMethodSymbol method, AnalysisState state)
    {
        if (state.Tier == AnalysisTier.Deep) return false;
        return method.IsAbstract
            || (method.IsVirtual && method.ContainingType.TypeKind
                == TypeKind.Interface)
            || method.ContainingType.TypeKind == TypeKind.Interface;
    }
}
