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

    public HashSet<ISymbol> FlattenedAdj { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> EdgeCounts { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> ElementSizes { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<ISymbol> UnboundedHeaps { get; } =
        new(SymbolEqualityComparer.Default);

    public HashSet<IMethodSymbol> Analyzing { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<IMethodSymbol, ComposedCost> Cache { get; } =
        new(SymbolEqualityComparer.Default);

    public int Depth { get; set; }

    public const int MaxDepth = 8;

    public List<InputDimension> Dimensions { get; } = [];

    public List<AnalysisWarning> Warnings { get; } = [];

    public List<BoundingSuggestion> Suggestions { get; } = [];

    public List<(AnalysisConfidence Cap, string Reason)> Notes { get; } =
        [];

    public List<ComplexityExpression> Retained { get; } = [];

    public ComplexityExpression? CurrentLoopBound { get; set; }

    public ComplexityExpression? FrontierBound { get; set; }

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

internal sealed class Cardinality
{
    public ComplexityExpression Seed { get; set; } = Cx.One;
    public ComplexityExpression Current { get; set; } = Cx.One;
    public ComplexityExpression Max { get; set; } = Cx.One;
}
