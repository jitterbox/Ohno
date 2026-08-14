using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

public sealed partial class CSharpMethodAnalyzer
{
    internal ComposedCost Analyze(IOperation operation, AnalysisState state)
    {
        return operation switch
        {
            IBlockOperation block => AnalyzeBlock(block, state),
            IForLoopOperation loop => AnalyzeFor(loop, state),
            IForEachLoopOperation loop => AnalyzeForEach(loop, state),
            IWhileLoopOperation loop => AnalyzeWhile(loop, state),
            IConditionalOperation cond => AnalyzeConditional(cond, state),
            IInvocationOperation call => AnalyzeInvocation(call, state),
            IObjectCreationOperation create => AnalyzeCreation(create, state),
            IVariableDeclaratorOperation decl => AnalyzeDeclarator(decl, state),
            IExpressionStatementOperation expr =>
                Analyze(expr.Operation, state),
            IReturnOperation ret when ret.ReturnedValue is not null =>
                Analyze(ret.ReturnedValue, state),
            IConversionOperation conv => Analyze(conv.Operand, state),
            ISwitchOperation sw => AnalyzeSwitch(sw, state),
            ITryOperation tryOp => AnalyzeTry(tryOp, state),
            IUsingOperation usingOp => Analyze(usingOp.Body, state),
            IForToLoopOperation loop => Analyze(loop.Body, state),
            _ => AnalyzeChildren(operation, state),
        };
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
        return AnalyzeLoop(loop.Body, bound, label, loop, state);
    }

    private ComposedCost AnalyzeForEach(
        IForEachLoopOperation loop, AnalysisState state)
    {
        if (SymbolKeys.IsQueryable(loop.Collection.Type))
            return QueryableLoop(loop, state);

        HeapBoundDetector.Detect(loop.Body, state);
        var (bound, label) = LoopBoundInferrer.Infer(loop, state);
        return AnalyzeLoop(loop.Body, bound, label, loop, state);
    }

    private ComposedCost AnalyzeLoop(
        IOperation body,
        ComplexityExpression bound,
        string label,
        IOperation loop,
        AnalysisState state)
    {
        var previous = state.CurrentLoopBound;
        state.CurrentLoopBound = bound;
        var bodyCost = Analyze(body, state);
        state.CurrentLoopBound = previous;
        NoteUnboundedHeaps(bound, state);
        return CostComposer.Loop(bound, bodyCost, label, RoslynSpans.Of(loop));
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
            state.Sizes[local] = Cx.One;
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
