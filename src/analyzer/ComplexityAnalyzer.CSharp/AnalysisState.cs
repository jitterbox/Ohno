using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

internal sealed class AnalysisState
{
    public AnalysisState(AnalysisTier tier)
    {
        Tier = tier;
    }

    public AnalysisTier Tier { get; }

    public Dictionary<ISymbol, ComplexityExpression> Sizes { get; } =
        new(SymbolEqualityComparer.Default);

    public Dictionary<ISymbol, ComplexityExpression> HeapBounds { get; } =
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

    public List<ComplexityExpression> Retained { get; } = [];

    public ComplexityExpression? CurrentLoopBound { get; set; }

    public ComplexityExpression SizeOf(ISymbol? symbol)
    {
        if (symbol is null) return Cx.Var("n");
        return Sizes.TryGetValue(symbol, out var size) ? size : Cx.Var("n");
    }
}
