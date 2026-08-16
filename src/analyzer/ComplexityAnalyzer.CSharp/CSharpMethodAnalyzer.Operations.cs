using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

public sealed partial class CSharpMethodAnalyzer
{
    /// <summary>
    /// Dispatches on <see cref="IOperation.Kind"/>. Prefer operation
    /// interfaces over syntax so <c>for</c>, <c>foreach</c>, and
    /// <c>while</c> share the same bound logic.
    /// </summary>
    /// <remarks>
    /// Edge cases:
    /// <list type="bullet">
    /// <item>
    /// <see cref="IForEachLoopOperation"/> also covers
    /// <c>await foreach</c>
    /// (<see href="https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream">async streams</see>).
    /// Pattern recognition treats those as opaque awaited work.
    /// </item>
    /// <item>
    /// <see cref="IForToLoopOperation"/> is Visual Basic's
    /// <c>For…To…Next</c>. C# does not produce it; the bound is not
    /// inferred if one appears.
    /// </item>
    /// <item>
    /// <see cref="ILocalFunctionOperation"/> is a declaration, not a
    /// call. Analyzing the body here would double-count nested
    /// generation (subsets, permutations, DFS).
    /// </item>
    /// <item>
    /// <see cref="IDynamicInvocationOperation"/> has no static target
    /// (<see href="https://learn.microsoft.com/dotnet/csharp/advanced-topics/interop/using-type-dynamic">dynamic</see>).
    /// </item>
    /// </list>
    /// </remarks>
    internal ComposedCost Analyze(IOperation operation, AnalysisState state)
    {
        state.Token.ThrowIfCancellationRequested();
        if (state.OperationDepth >= AnalysisState.MaxOperationDepth)
            return TooDeep(operation, state);

        state.OperationDepth++;
        try
        {
            return AnalyzeOperation(operation, state);
        }
        finally
        {
            state.OperationDepth--;
        }
    }

    /// <summary>
    /// Refuses to recurse further rather than risking the stack. The
    /// result is an honest unknown, not a constant: work that was not
    /// examined has not been shown to be free.
    /// </summary>
    private static ComposedCost TooDeep(
        IOperation operation, AnalysisState state)
    {
        state.Note(
            AnalysisConfidence.Unknown,
            "The expression nests deeper than the analyzer walks, so "
            + "part of this method was not examined.");
        return new ComposedCost
        {
            Time = Cx.Unknown("nesting depth"),
            Space = Cx.Unknown("nesting depth"),
            Confidence = AnalysisConfidence.Unknown,
            Evidence = ComplexityEvidence.Leaf(
                "depth",
                "nesting limit reached",
                Cx.Unknown("nesting depth"),
                RoslynSpans.Of(operation)),
            Warnings = new[]
            {
                new AnalysisWarning(
                    "Analysis stopped at "
                    + AnalysisState.MaxOperationDepth
                    + " levels of nesting.",
                    RoslynSpans.Of(operation)),
            },
        };
    }

    private ComposedCost AnalyzeOperation(
        IOperation operation, AnalysisState state)
    {
        if (operation.Syntax is not null
            && state.UnreachableSyntax.Contains(operation.Syntax))
        {
            return ComposedCost.Unit(
                "dead", "unreachable", RoslynSpans.Of(operation));
        }

        return operation switch
        {
            IBlockOperation block => AnalyzeBlock(block, state),
            IForLoopOperation loop => AnalyzeFor(loop, state),
            IForEachLoopOperation loop => AnalyzeForEach(loop, state),
            IWhileLoopOperation loop => AnalyzeWhile(loop, state),
            IConditionalOperation cond => AnalyzeConditional(cond, state),
            IInvocationOperation call => AnalyzeInvocation(call, state),
            IDynamicInvocationOperation dyn => UnknownDynamic(dyn),
            IPropertyReferenceOperation prop =>
                AnalyzePropertyRead(prop, state),
            IBinaryOperation binary => AnalyzeBinary(binary, state),
            IObjectCreationOperation create => AnalyzeCreation(create, state),
            IArrayCreationOperation array => AnalyzeArrayCreate(array, state),
            ISimpleAssignmentOperation assign =>
                AnalyzeAssignment(assign, state),
            IVariableDeclaratorOperation decl => AnalyzeDeclarator(decl, state),
            IExpressionStatementOperation expr =>
                Analyze(expr.Operation, state),
            IReturnOperation ret when ret.ReturnedValue is not null =>
                Analyze(ret.ReturnedValue, state),
            IConversionOperation conv => Analyze(conv.Operand, state),
            ISwitchOperation sw => AnalyzeSwitch(sw, state),
            ITryOperation tryOp => AnalyzeTry(tryOp, state),
            IUsingOperation usingOp => Analyze(usingOp.Body, state),
            IForToLoopOperation loop => AnalyzeForTo(loop, state),
            ICollectionExpressionOperation coll =>
                AnalyzeCollection(coll, state),
            IInterpolatedStringOperation interp =>
                AnalyzeInterpolated(interp, state),
            IIncrementOrDecrementOperation inc =>
                Analyze(inc.Target, state),
            ILocalFunctionOperation local => ComposedCost.Unit(
                "local",
                local.Symbol.Name,
                RoslynSpans.Of(local)),
            _ => AnalyzeChildren(operation, state),
        };
    }

    private ComposedCost AnalyzeCollection(
        ICollectionExpressionOperation coll, AnalysisState state)
    {
        var size = CollectionSize(coll, state);
        state.Retained.Add(size);
        return ComposedCost.Of(
            size, size, "alloc", "collection", RoslynSpans.Of(coll));
    }

    private static ComplexityExpression CollectionSize(
        ICollectionExpressionOperation coll, AnalysisState state)
    {
        if (coll.Elements.Length == 0) return Cx.One;
        return Cx.Add(coll.Elements.Select(e =>
            e is ISpreadOperation spread
                ? SizeResolver.Resolve(spread.Operand, state)
                : Cx.One));
    }

    private ComposedCost AnalyzeInterpolated(
        IInterpolatedStringOperation interp, AnalysisState state)
    {
        var sizes = interp.Parts.Select(p =>
            p is IInterpolationOperation hole
                ? SizeResolver.Resolve(hole.Expression, state)
                : Cx.One);
        var size = Cx.Add(sizes);
        return ComposedCost.Of(
            size, Cx.One, "string", "interpolated",
            RoslynSpans.Of(interp));
    }

    private ComposedCost AnalyzeBlock(
        IBlockOperation block, AnalysisState state)
    {
        var parts = block.Operations
            .Select(op => Analyze(op, state))
            .ToArray();
        return CostComposer.Sequential(parts, RoslynSpans.Of(block));
    }

    private ComposedCost AnalyzeChildren(
        IOperation operation, AnalysisState state)
    {
        var parts = operation.ChildOperations
            .Select(op => Analyze(op, state))
            .ToArray();
        return CostComposer.Sequential(parts, RoslynSpans.Of(operation));
    }

    private ComposedCost AnalyzeFor(
        IForLoopOperation loop, AnalysisState state)
    {
        HeapBoundDetector.Detect(loop.Body, state);
        var (bound, label) = LoopBoundInferrer.Infer(loop, state);
        return AnalyzeLoop(loop.Body, bound, label, loop, state);
    }

    private ComposedCost AnalyzeWhile(
        IWhileLoopOperation loop, AnalysisState state)
    {
        HeapBoundDetector.Detect(loop.Body, state);
        var (bound, label) = LoopBoundInferrer.Infer(loop, state);
        if (state.CurrentLoopBound is not null
            && LoopBoundInferrer.IsProgressOnly(loop.Body)
            && !LoopBoundInferrer.ResetsCounter(loop, state))
        {
            bound = Cx.One;
            label = "amortized pointer step";
        }

        var previousFrontier = state.FrontierBound;
        if (label.Contains("frontier", StringComparison.Ordinal))
            state.FrontierBound = bound;
        var cost = AnalyzeLoop(loop.Body, bound, label, loop, state);
        state.FrontierBound = previousFrontier;
        return cost;
    }

    private ComposedCost AnalyzeForEach(
        IForEachLoopOperation loop, AnalysisState state)
    {
        if (SymbolKeys.IsQueryable(loop.Collection.Type))
            return QueryableLoop(loop, state);

        HeapBoundDetector.Detect(loop.Body, state);
        NoteElementSize(loop, state);
        var (bound, label) = LoopBoundInferrer.Infer(loop, state);
        var move = MoveNextCost(loop, state);
        if (move is null)
            return AnalyzeLoop(loop.Body, bound, label, loop, state);

        var previous = state.CurrentLoopBound;
        var previousBody = state.CurrentLoopBody;
        state.CurrentLoopBound = bound;
        state.CurrentLoopBody = loop.Body;
        var body = Analyze(loop.Body, state);
        state.CurrentLoopBound = previous;
        state.CurrentLoopBody = previousBody;
        var combined = CostComposer.Sequential(
            new[] { move, body }, RoslynSpans.Of(loop.Body));
        NoteUnboundedHeaps(bound, state);
        NoteLoopShape(label, state);
        return CostComposer.Loop(
            bound, combined, label, RoslynSpans.Of(loop));
    }

    private ComposedCost AnalyzeLoop(
        IOperation body,
        ComplexityExpression bound,
        string label,
        IOperation loop,
        AnalysisState state)
    {
        var previous = state.CurrentLoopBound;
        var previousBody = state.CurrentLoopBody;
        state.CurrentLoopBound = bound;
        state.CurrentLoopBody = body;
        var bodyCost = Analyze(body, state);
        state.CurrentLoopBound = previous;
        state.CurrentLoopBody = previousBody;
        NoteUnboundedHeaps(bound, state);
        NoteLoopShape(label, state);
        return CostComposer.Loop(bound, bodyCost, label, RoslynSpans.Of(loop));
    }

    private ComposedCost AnalyzeForTo(
        IForToLoopOperation loop, AnalysisState state)
    {
        state.Note(
            AnalysisConfidence.Medium,
            "A for-to loop bound was not inferred from the range.");
        return Analyze(loop.Body, state);
    }

    private static void NoteLoopShape(string label, AnalysisState state)
    {
        if (label.Contains("log", StringComparison.Ordinal))
        {
            state.Note(
                AnalysisConfidence.Medium,
                "Logarithmic bound assumed from a doubling or "
                + "halving update; a different update may miss this.");
        }

        if (label.Contains("unknown bound", StringComparison.Ordinal))
        {
            state.Note(
                AnalysisConfidence.Medium,
                "Loop bound could not be read from the condition; "
                + "n was assumed.");
        }

        if (label.Contains("frontier", StringComparison.Ordinal))
        {
            state.Note(
                AnalysisConfidence.Medium,
                "Frontier bound assumed from a visited array and "
                + "queue.Count; another traversal shape may miss this.");
        }
    }

    private ComposedCost AnalyzeConditional(
        IConditionalOperation cond, AnalysisState state)
    {
        var condition = Analyze(cond.Condition, state);
        var whenTrue = Analyze(cond.WhenTrue, state);
        var whenFalse = cond.WhenFalse is null
            ? null
            : Analyze(cond.WhenFalse, state);
        return CostComposer.Conditional(
            condition, whenTrue, whenFalse, RoslynSpans.Of(cond));
    }

    private ComposedCost AnalyzeSwitch(
        ISwitchOperation sw, AnalysisState state)
    {
        var parts = sw.Cases
            .Select(c => Analyze(c, state))
            .ToArray();
        if (parts.Length == 0)
            return ComposedCost.Unit("switch", "switch", RoslynSpans.Of(sw));
        var worst = parts[0];
        foreach (var part in parts.Skip(1))
        {
            worst = new ComposedCost
            {
                Time = CostComposer.MaxExpr(worst.Time, part.Time),
                Space = CostComposer.Peak(new[] { worst.Space, part.Space }),
                Confidence = ComposedCost.Min(
                    worst.Confidence, part.Confidence),
                Evidence = part.Evidence,
                Warnings = worst.Warnings.Concat(part.Warnings).ToArray(),
                Suggestions = worst.Suggestions
                    .Concat(part.Suggestions)
                    .ToArray(),
            };
        }

        return worst;
    }

    private ComposedCost AnalyzeTry(
        ITryOperation tryOp, AnalysisState state)
    {
        var parts = new List<ComposedCost> { Analyze(tryOp.Body, state) };
        parts.AddRange(tryOp.Catches.Select(c => Analyze(c, state)));
        if (tryOp.Finally is not null)
            parts.Add(Analyze(tryOp.Finally, state));
        return CostComposer.Sequential(parts, RoslynSpans.Of(tryOp));
    }

    private ComposedCost AnalyzeArrayCreate(
        IArrayCreationOperation array, AnalysisState state)
    {
        var size = SizeResolver.Resolve(array, state);
        state.Retained.Add(size);
        return ComposedCost.Of(
            size,
            size,
            "alloc",
            "array",
            RoslynSpans.Of(array));
    }

    private ComposedCost AnalyzePropertyRead(
        IPropertyReferenceOperation prop, AnalysisState state)
    {
        if (prop.Parent is IAssignmentOperation)
            return AnalyzeChildren(prop, state);
        var getter = prop.Property.GetMethod;
        if (getter is null) return AnalyzeChildren(prop, state);
        if (getter.DeclaringSyntaxReferences.Length > 0)
        {
            return prop.SemanticModel is { } model
                ? AnalyzeSymbol(getter, model, state)
                : AnalyzeChildren(prop, state);
        }

        return MetadataPropertyRead(prop, getter, state);
    }

    /// <summary>
    /// A property whose getter is not in this compilation. Reading a
    /// stored field or a fixed-size view is O(1); everything else is
    /// executable code with no summary, and gets carried as a call
    /// rather than assumed free.
    /// </summary>
    private ComposedCost MetadataPropertyRead(
        IPropertyReferenceOperation prop,
        IMethodSymbol getter,
        AnalysisState state)
    {
        // An auto-property on a type declared in this compilation has
        // no getter syntax, but it is a field read, not unresolved work.
        if (prop.Property.ContainingType?.OriginalDefinition
            .DeclaringSyntaxReferences.Length > 0)
        {
            return AnalyzeChildren(prop, state);
        }

        var type = SymbolKeys.TypeName(prop.Property.ContainingType);
        var key = SymbolKeys.ForMethod(getter.OriginalDefinition);
        if (key is not null && _catalog.TryGet(key, out var entry))
            return CatalogProperty(prop, entry, state);
        if (ConstantTimePrimitives.IsConstantAccessor(type, getter.Name))
            return AnalyzeChildren(prop, state);

        var children = AnalyzeChildren(prop, state);
        var call = UnknownCall(
            getter.Name,
            RoslynSpans.Of(prop),
            $"{type ?? prop.Property.Name}.{prop.Property.Name} has no "
            + "cost summary, so reading it is carried as a call instead "
            + "of assumed constant");
        return CostComposer.Sequential(
            new[] { children, call }, RoslynSpans.Of(prop));
    }

    private ComposedCost CatalogProperty(
        IPropertyReferenceOperation prop,
        CatalogEntry entry,
        AnalysisState state)
    {
        var size = SizeResolver.Resolve(prop.Instance, state);
        var confidence = NoteCatalogKind(entry, state);
        var cataloged = ComposedCost.Of(
            entry.Time.Bind(size),
            entry.Space.Bind(size),
            "prop",
            prop.Property.Name,
            RoslynSpans.Of(prop),
            confidence);
        var children = AnalyzeChildren(prop, state);
        return CostComposer.Sequential(
            new[] { children, cataloged }, RoslynSpans.Of(prop));
    }

    private ComposedCost AnalyzeBinary(
        IBinaryOperation binary, AnalysisState state)
    {
        var op = binary.OperatorMethod;
        if (op is null || op.DeclaringSyntaxReferences.Length == 0)
            return AnalyzeChildren(binary, state);
        if (binary.SemanticModel is not { } model)
            return AnalyzeChildren(binary, state);
        var callee = AnalyzeSymbol(op, model, state);
        return CostComposer.Sequential(
            new[]
            {
                Analyze(binary.LeftOperand, state),
                Analyze(binary.RightOperand, state),
                callee,
            },
            RoslynSpans.Of(binary));
    }

    private ComposedCost? MoveNextCost(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var collection = loop.Collection.Type as INamedTypeSymbol;
        if (collection is null) return null;
        var move = collection.GetTypeMembers()
            .SelectMany(t => t.GetMembers("MoveNext"))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.DeclaringSyntaxReferences.Length > 0);
        if (move is null)
        {
            var getEnum = collection.GetMembers("GetEnumerator")
                .OfType<IMethodSymbol>()
                .FirstOrDefault();
            move = getEnum?.ReturnType.GetMembers("MoveNext")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.DeclaringSyntaxReferences.Length > 0);
        }

        if (move is null) return null;
        if (loop.SemanticModel is not { } model) return null;
        return AnalyzeSymbol(move, model, state);
    }

    private static ComposedCost UnknownDynamic(
        IDynamicInvocationOperation call)
    {
        return UnknownCall(
            "dynamic",
            RoslynSpans.Of(call),
            "dynamic or unresolved dispatch");
    }

    private ComposedCost AnalyzeAssignment(
        ISimpleAssignmentOperation assign, AnalysisState state)
    {
        NoteIndexerWrite(assign.Target, state);
        return CostComposer.Sequential(
            new[]
            {
                Analyze(assign.Target, state),
                Analyze(assign.Value, state),
            },
            RoslynSpans.Of(assign));
    }

    private static void NoteIndexerWrite(
        IOperation target, AnalysisState state)
    {
        if (SizeResolver.Unwrap(target) is not IPropertyReferenceOperation prop)
            return;
        if (!prop.Property.IsIndexer) return;
        var symbol = SizeResolver.TargetSymbol(prop.Instance);
        if (symbol is null || state.CurrentLoopBound is null) return;
        state.Sizes[symbol] = state.CurrentLoopBound;
        state.Retained.Add(state.CurrentLoopBound);
    }

    private ComposedCost AnalyzeDeclarator(
        IVariableDeclaratorOperation decl, AnalysisState state)
    {
        var initializer = decl.Initializer?.Value;
        if (initializer is null)
            return ComposedCost.Unit("decl", decl.Symbol.Name, RoslynSpans.Of(decl));

        TrackAlias(decl.Symbol, initializer, state);
        TrackCreation(decl.Symbol, initializer, state);
        return Analyze(initializer, state);
    }

    private static void NoteElementSize(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var local = loop.LoopControlVariable switch
        {
            IVariableDeclaratorOperation d => d.Symbol,
            ILocalReferenceOperation l => l.Local,
            _ => null,
        };
        if (local is null || state.Sizes.ContainsKey(local)) return;
        if (!DimensionInferrer.IsCollection(local.Type)) return;
        state.Sizes[local] = DimensionInferrer.Fresh(
            state, $"{local.Name}.Length");
    }

    private static void TrackAlias(
        ISymbol local, IOperation value, AnalysisState state)
    {
        var unwrapped = SizeResolver.Unwrap(value);
        if (unwrapped is IPropertyReferenceOperation prop
            && prop.Property.Name is "Count" or "Length")
        {
            var owner = SizeResolver.TargetSymbol(prop.Instance);
            if (owner is not null)
                state.Sizes[local] = state.SizeOf(owner);
        }
    }

    private static void TrackCreation(
        ISymbol local, IOperation value, AnalysisState state)
    {
        var unwrapped = SizeResolver.Unwrap(value);
        if (unwrapped is IObjectCreationOperation create
            && IsPriorityQueue(create.Type))
        {
            if (create.Arguments.Length == 1)
            {
                state.Sizes[local] = SizeResolver.Resolve(
                    create.Arguments[0].Value, state);
                return;
            }

            state.Sizes[local] = Cx.One;
            return;
        }

        if (unwrapped is IArrayCreationOperation array)
        {
            state.Sizes[local] = SizeResolver.Resolve(array, state);
            return;
        }

        if (unwrapped is IInvocationOperation call
            && call.Type is not null
            && DimensionInferrer.IsCollection(call.Type))
        {
            state.Sizes[local] = SizeResolver.Resolve(call, state);
        }
    }

    private static bool IsPriorityQueue(ITypeSymbol? type) =>
        SymbolKeys.TypeName(type)
            == "System.Collections.Generic.PriorityQueue`2";

    private ComposedCost QueryableLoop(
        IForEachLoopOperation loop, AnalysisState state)
    {
        var span = RoslynSpans.Of(loop);
        state.Warnings.Add(new AnalysisWarning(
            "Query executes database-side; complexity depends on " +
            "provider, schema, and indexes.",
            span));
        return new ComposedCost
        {
            Time = Cx.Unknown("database"),
            Space = Cx.Unknown("database"),
            Confidence = AnalysisConfidence.Unknown,
            Evidence = ComplexityEvidence.Leaf(
                "linq",
                "IQueryable enumeration",
                Cx.Unknown("database"),
                span),
            Warnings = state.Warnings.ToArray(),
        };
    }

    private static void NoteUnboundedHeaps(
        ComplexityExpression bound, AnalysisState state)
    {
        if (state.UnboundedHeaps.Count == 0) return;
        var k = Cx.Var("k");
        state.Suggestions.Add(new BoundingSuggestion(
            "Unbounded priority queue grows with the input. " +
            "A worst-case of O(n log n) can be reduced by bounding.",
            "dequeue when Count > k",
            ComplexitySimplifier.Simplify(Cx.Mul(bound, Cx.Log(k))),
            k));
    }
}
