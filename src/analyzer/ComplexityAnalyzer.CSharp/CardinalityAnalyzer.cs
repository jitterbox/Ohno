using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Companion CFG / dataflow pass: reachability, loop indices,
/// and per-collection SizeDelta (seed, current, max).
/// </summary>
internal static class CardinalityAnalyzer
{
    public static void Analyze(
        IOperation body,
        SemanticModel model,
        AnalysisState state)
    {
        MarkUnreachable(body, state);
        MarkLoopIndices(body, model, state);
        ApplyTree(body, Cx.One, state);
        HeapBoundDetector.Detect(body, state);
        WorklistBoundDetector.Detect(body, state);
        Publish(state);
    }

    private static void MarkUnreachable(
        IOperation body, AnalysisState state)
    {
        var cfg = TryCfg(body);
        if (cfg is null) return;
        foreach (var block in cfg.Blocks)
        {
            if (block.IsReachable) continue;
            foreach (var op in block.Operations)
            {
                if (op.Syntax is not null)
                    state.UnreachableSyntax.Add(op.Syntax);
            }
        }
    }

    private static ControlFlowGraph? TryCfg(IOperation body)
    {
        try
        {
            return body switch
            {
                IBlockOperation block =>
                    ControlFlowGraph.Create(block),
                IMethodBodyOperation method =>
                    ControlFlowGraph.Create(method),
                _ => ControlFlowGraph.Create(
                    body.Syntax, body.SemanticModel!),
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void MarkLoopIndices(
        IOperation body,
        SemanticModel model,
        AnalysisState state)
    {
        foreach (var inc in Increments(body))
        {
            if (inc is not null)
                state.LoopIndices.Add(inc);
        }

        var syntax = body.Syntax;
        DataFlowAnalysis? data = null;
        if (syntax is StatementSyntax statement)
            data = model.AnalyzeDataFlow(statement);
        else if (syntax is ExpressionSyntax expression)
            data = model.AnalyzeDataFlow(expression);
        else if (syntax.Parent is StatementSyntax parent)
            data = model.AnalyzeDataFlow(parent);
        if (data is null) return;
        if (!data.Succeeded) return;
        foreach (var written in data.WrittenInside)
        {
            if (!IsIntegral(written)) continue;
            if (state.LoopIndices.Contains(written)) continue;
            if (IsIncremented(body, written))
                state.LoopIndices.Add(written);
        }
    }

    private static IEnumerable<ISymbol?> Increments(IOperation root)
    {
        foreach (var op in Walk(root))
        {
            if (op is IForLoopOperation loop)
            {
                foreach (var bottom in loop.AtLoopBottom)
                    yield return IncrementTarget(bottom);
            }

            yield return IncrementTarget(op);
        }
    }

    private static ISymbol? IncrementTarget(IOperation operation)
    {
        var op = SizeResolver.Unwrap(operation);
        if (op is IExpressionStatementOperation expr)
            op = SizeResolver.Unwrap(expr.Operation);
        return op switch
        {
            IIncrementOrDecrementOperation inc =>
                SizeResolver.TargetSymbol(inc.Target),
            ICompoundAssignmentOperation c
                when c.OperatorKind is BinaryOperatorKind.Add
                    or BinaryOperatorKind.Subtract =>
                SizeResolver.TargetSymbol(c.Target),
            _ => null,
        };
    }

    private static bool IsIncremented(
        IOperation body, ISymbol symbol) =>
        Increments(body).Any(s =>
            SymbolEqualityComparer.Default.Equals(s, symbol));

    private static bool IsIntegral(ISymbol symbol)
    {
        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol p => p.Type,
            _ => null,
        };
        return type?.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_UInt32
            or SpecialType.System_Int16;
    }

    private static void ApplyTree(
        IOperation operation,
        ComplexityExpression bound,
        AnalysisState state)
    {
        if (IsUnreachable(operation, state)) return;
        ApplyNode(operation, bound, state);
        var next = LoopBound(operation, state) ?? bound;
        foreach (var child in operation.ChildOperations)
            ApplyTree(child, next, state);
    }

    private static void ApplyNode(
        IOperation operation,
        ComplexityExpression bound,
        AnalysisState state)
    {
        switch (operation)
        {
            case IInvocationOperation call:
                ApplyCall(call, bound, state);
                return;
            case IObjectCreationOperation create:
                ApplyCtor(create, bound, state);
                return;
        }
    }

    private static ComplexityExpression? LoopBound(
        IOperation operation, AnalysisState state)
    {
        return operation switch
        {
            IForEachLoopOperation loop =>
                SizeResolver.Resolve(loop.Collection, state),
            IForLoopOperation loop =>
                LoopBoundInferrer.Infer(loop, state).Bound,
            IWhileLoopOperation loop =>
                LoopBoundInferrer.Infer(loop, state).Bound,
            _ => null,
        };
    }

    private static void ApplyCall(
        IInvocationOperation call,
        ComplexityExpression bound,
        AnalysisState state)
    {
        var delta = DeltaOf(call.TargetMethod, state);
        var symbol = SizeResolver.TargetSymbol(call.Instance);
        if (symbol is null || delta == SizeDeltaKind.None) return;
        var card = state.Card(symbol);
        ApplyDelta(card, delta, bound, SourceSize(call, state));
        NoteEdge(call, symbol, bound, delta, state);
    }

    private static void ApplyCtor(
        IObjectCreationOperation create,
        ComplexityExpression bound,
        AnalysisState state)
    {
        var ctor = create.Constructor;
        if (ctor is null) return;
        var delta = DeltaOf(ctor, state);
        var symbol = AssignedSymbol(create);
        if (symbol is null) return;
        var card = state.Card(symbol);
        if (delta == SizeDeltaKind.Replace)
        {
            var source = SourceSize(create, state);
            card.Seed = source;
            card.Current = source;
            card.Max = Peak(card.Max, source);
            return;
        }

        ApplyDelta(card, delta, bound, Cx.One);
    }

    private static void ApplyDelta(
        Cardinality card,
        SizeDeltaKind delta,
        ComplexityExpression bound,
        ComplexityExpression source)
    {
        switch (delta)
        {
            case SizeDeltaKind.Increment:
                card.Current = Peak(card.Current, bound);
                card.Max = Peak(card.Max, card.Current);
                if (card.Seed is ConstantExpression)
                    card.Seed = card.Max;
                return;
            case SizeDeltaKind.Decrement:
                return;
            case SizeDeltaKind.Clear:
                card.Current = Cx.One;
                return;
            case SizeDeltaKind.Replace:
                card.Seed = source;
                card.Current = source;
                card.Max = Peak(card.Max, source);
                return;
        }
    }

    private static void NoteEdge(
        IInvocationOperation call,
        ISymbol target,
        ComplexityExpression bound,
        SizeDeltaKind delta,
        AnalysisState state)
    {
        if (delta != SizeDeltaKind.Increment) return;
        if (call.TargetMethod.Name != "Add") return;
        var owner = ArrayOwner(call.Instance);
        if (owner is null) return;
        state.EdgeCounts[owner] = Peak(
            state.EdgeCounts.GetValueOrDefault(owner) ?? Cx.One,
            bound);
    }

    private static ISymbol? ArrayOwner(IOperation? instance)
    {
        var op = SizeResolver.Unwrap(instance);
        if (op is IArrayElementReferenceOperation element)
            return SizeResolver.TargetSymbol(element.ArrayReference);
        return null;
    }

    private static SizeDeltaKind DeltaOf(
        IMethodSymbol method, AnalysisState state)
    {
        var key = SymbolKeys.ForMethod(method.OriginalDefinition);
        if (key is not null
            && state.Catalog.TryGet(key, out var entry))
        {
            return entry.Delta;
        }

        return method.Name switch
        {
            "Add" or "Enqueue" or "Push" or "set_Item" =>
                SizeDeltaKind.Increment,
            "Remove" or "RemoveAt" or "Dequeue" or "Pop"
                or "TryDequeue" or "TryPop" =>
                SizeDeltaKind.Decrement,
            "Clear" => SizeDeltaKind.Clear,
            _ => SizeDeltaKind.None,
        };
    }

    private static ComplexityExpression SourceSize(
        IInvocationOperation call, AnalysisState state)
    {
        if (call.Arguments.Length == 0) return Cx.One;
        return SizeResolver.Resolve(call.Arguments[0].Value, state);
    }

    private static ComplexityExpression SourceSize(
        IObjectCreationOperation create, AnalysisState state)
    {
        if (create.Arguments.Length == 0) return Cx.One;
        return SizeResolver.Resolve(create.Arguments[0].Value, state);
    }

    private static ISymbol? AssignedSymbol(
        IObjectCreationOperation create)
    {
        var parent = create.Parent;
        if (parent is IVariableInitializerOperation init
            && init.Parent is IVariableDeclaratorOperation decl)
        {
            return decl.Symbol;
        }

        if (parent is ISimpleAssignmentOperation assign)
            return SizeResolver.TargetSymbol(assign.Target);
        return null;
    }

    private static void Publish(AnalysisState state)
    {
        foreach (var (symbol, card) in state.Cardinalities)
        {
            state.Sizes[symbol] = card.Max;
            if (state.HeapBounds.ContainsKey(symbol))
                state.Sizes[symbol] = state.HeapBounds[symbol];
        }
    }

    private static bool IsUnreachable(
        IOperation operation, AnalysisState state) =>
        operation.Syntax is not null
        && state.UnreachableSyntax.Contains(operation.Syntax);

    private static ComplexityExpression Peak(
        ComplexityExpression left, ComplexityExpression right) =>
        CostComposer.Peak(new[] { left, right });

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
