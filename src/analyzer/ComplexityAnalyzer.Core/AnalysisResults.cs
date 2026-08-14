namespace ComplexityAnalyzer.Core;

/// <summary>
/// Confidence in a complexity result. Ordered so that combining results
/// can take the minimum.
/// </summary>
public enum AnalysisConfidence
{
    /// <summary>A meaningful bound cannot be inferred.</summary>
    Unknown = 0,

    /// <summary>One or more potentially significant operations are unresolved.</summary>
    Low = 1,

    /// <summary>The dominant complexity is clear but some details are approximated.</summary>
    Medium = 2,

    /// <summary>All meaningful operations were statically resolved with known costs.</summary>
    High = 3,
}

/// <summary>A 0-based source span (LSP convention, matching Roslyn LinePosition).</summary>
public sealed record LineSpan(int StartLine, int StartCharacter, int EndLine, int EndCharacter);

/// <summary>
/// A symbolic input dimension with its meaning preserved,
/// e.g. n = nums.Length or k = method parameter "k".
/// </summary>
public sealed record InputDimension(string Variable, string Meaning);

/// <summary>
/// A node in the derivation tree explaining how a result was computed.
/// Children roll up mathematically into the parent's cost; the tree doubles
/// as the UI nesting model for inline annotations.
/// </summary>
public sealed record ComplexityEvidence(
    string Kind,
    string Label,
    ComplexityExpression Cost,
    LineSpan? Span,
    IReadOnlyList<ComplexityEvidence> Children)
{
    public static ComplexityEvidence Leaf(
        string kind, string label, ComplexityExpression cost, LineSpan? span = null) =>
        new(kind, label, cost, span, Array.Empty<ComplexityEvidence>());
}

/// <summary>A non-fatal caveat produced during analysis.</summary>
public sealed record AnalysisWarning(string Message, LineSpan? Span = null);

/// <summary>
/// An opportunity to reduce the worst-case bound by adding an explicit
/// bounding condition, e.g. capping a priority queue at k elements.
/// </summary>
public sealed record BoundingSuggestion(
    string Description,
    string Condition,
    ComplexityExpression ResultingTime,
    ComplexityExpression ResultingSpace);

/// <summary>The structured result of analyzing a single function.</summary>
public sealed record ComplexityResult(
    ComplexityExpression Time,
    ComplexityExpression AuxiliarySpace,
    AnalysisConfidence Confidence,
    IReadOnlyList<InputDimension> Dimensions,
    ComplexityEvidence Evidence,
    IReadOnlyList<AnalysisWarning> Warnings,
    IReadOnlyList<BoundingSuggestion> BoundingSuggestions);
