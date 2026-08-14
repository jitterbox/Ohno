using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.DotNet;

public enum CostKind
{
    Exact,
    Amortized,
    Expected,
}

/// <summary>
/// Known complexity of a BCL or LINQ member. Size templates use the
/// receiver (or source) dimension: Constant, Receiver, or LogReceiver.
/// </summary>
public sealed record CatalogEntry(
    string Key,
    CostTemplate Time,
    CostTemplate Space,
    CostKind Kind,
    bool Deferred = false,
    bool Materializes = false,
    bool Sorts = false,
    bool IsQueryable = false);

public enum SizeKind
{
    Constant,
    Receiver,
    LogReceiver,
}

public sealed record CostTemplate(SizeKind Size, int Power = 1)
{
    public ComplexityExpression Bind(ComplexityExpression receiverSize)
    {
        return Size switch
        {
            SizeKind.Constant => Cx.One,
            SizeKind.Receiver when Power == 1 => receiverSize,
            SizeKind.Receiver => Cx.Pow(receiverSize, Cx.Constant(Power)),
            SizeKind.LogReceiver => Cx.Log(receiverSize),
            _ => Cx.One,
        };
    }
}
