using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

internal sealed class AnalysisState
{
    public AnalysisState(AnalysisTier tier)
    {
        Tier = tier;
    }

    public AnalysisTier Tier { get; }

    public OperationCatalog Catalog { get; set; } =
        OperationCatalog.CreateDefault();

    public Dictionary<ISymbol, ComplexityExpression> Sizes { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> HeapBounds { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> WorklistBounds { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, Cardinality> Cardinalities { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<ISymbol> LoopIndices { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<SyntaxNode> UnreachableSyntax { get; } = [];

    /// <summary>
    /// Structural facts about a loop body, cached per operation.
    /// </summary>
    /// <remarks>
    /// Only shape lives here — never a resolved bound. The cardinality
    /// pass and the cost walk both ask for a loop's bound, and sizes
    /// are still being learned in between, so caching the bound itself
    /// would freeze the earlier, less informed answer. The shape does
    /// not depend on <see cref="Sizes"/>, so it is safe to reuse.
    /// </remarks>
    public Dictionary<IOperation, LoopShape> LoopShapes { get; } = new();

    public HashSet<ISymbol> FlattenedAdj { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> EdgeCounts { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> ElementSizes { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<ISymbol> UnboundedHeaps { get; } =
        new(SymbolEqualityComparer.Default);

    /// <summary>Regexes built with a linear-time engine.</summary>
    public HashSet<ISymbol> LinearRegexes { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<IMethodSymbol> Analyzing { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<IMethodSymbol, ComposedCost> Cache { get; } =
        new(SymbolEqualityComparer.Default);

    public int Depth { get; set; }

    public const int MaxDepth = 8;

    /// <summary>Current nesting inside one method's operation tree.</summary>
    public int OperationDepth { get; set; }

    /// <summary>
    /// Cap on operation-tree recursion. <see cref="MaxDepth"/> bounds
    /// how many methods deep the walk goes; this bounds how deep a
    /// single body may nest. Generated code — a long chained
    /// expression, a deeply nested initializer — can otherwise recurse
    /// far enough to overflow the stack, and the stack belongs to the
    /// analyzer server process, so the whole session dies with it.
    /// </summary>
    public const int MaxOperationDepth = 400;

    /// <summary>Cancels a walk whose result nobody is waiting for.</summary>
    public CancellationToken Token { get; set; }

    public List<InputDimension> Dimensions { get; } = [];

    public List<AnalysisWarning> Warnings { get; } = [];

    public List<BoundingSuggestion> Suggestions { get; } = [];

    public List<(AnalysisConfidence Cap, string Reason)> Notes { get; } =
        [];

    public List<ComplexityExpression> Retained { get; } = [];

    public ComplexityExpression? CurrentLoopBound { get; set; }

    /// <summary>
    /// The body of the loop currently being walked, so a nested loop
    /// can ask whether its counter is re-seeded on each outer step.
    /// </summary>
    public IOperation? CurrentLoopBody { get; set; }

    public ComplexityExpression? FrontierBound { get; set; }

    public string? RecurrenceId { get; set; }

    public string? RecurrenceLabel { get; set; }

    public string? RecurrenceBound { get; set; }

    public bool IsSelection { get; set; }

    public void Note(AnalysisConfidence cap, string reason)
    {
        if (Notes.Any(n => n.Reason == reason)) return;
        Notes.Add((cap, reason));
    }

    public ComplexityExpression SizeOf(ISymbol? symbol)
    {
        if (symbol is null) return Cx.Var("n");
        return Sizes.TryGetValue(symbol, out var size) ? size : Cx.Var("n");
    }

    public ComplexityExpression ElementSizeOf(
        ISymbol owner, string meaning)
    {
        if (FlattenedAdj.Contains(owner))
            return Cx.One;
        if (ElementSizes.TryGetValue(owner, out var size))
            return size;
        var fresh = DimensionInferrer.Fresh(this, meaning);
        ElementSizes[owner] = fresh;
        Note(
            AnalysisConfidence.Medium,
            "An inner collection size was introduced as a fresh "
            + "dimension, not a proven edge count.");
        return fresh;
    }

    public Cardinality Card(ISymbol symbol)
    {
        if (Cardinalities.TryGetValue(symbol, out var card))
            return card;
        var created = new Cardinality();
        Cardinalities[symbol] = created;
        return created;
    }
}

/// <summary>
/// Shape facts a loop body can be asked for, all gathered in one pass
/// instead of one full sub-tree walk per question.
/// </summary>
/// <param name="Halves">A <c>/= 2</c>, <c>&gt;&gt;= 1</c>, or <c>x = x / 2</c> update.</param>
/// <param name="MidSplits">A division by two, as binary search does.</param>
/// <param name="ShrinksBound">An assignment moving a local by ±.</param>
/// <param name="VisitedArray">The array a visit mark is written into.</param>
internal sealed record LoopShape(
    bool Halves,
    bool MidSplits,
    bool ShrinksBound,
    ISymbol? VisitedArray);

internal sealed class Cardinality
{
    public ComplexityExpression Seed { get; set; } = Cx.One;
    public ComplexityExpression Current { get; set; } = Cx.One;
    public ComplexityExpression Max { get; set; } = Cx.One;
}
