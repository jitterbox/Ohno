namespace ComplexityAnalyzer.Server;

public sealed record InitializeParams(string? ClientInfo);

public sealed record InitializeResult(string ServerName, string AnalyzerVersion);

public sealed record AnalyzeRequest(
    string Uri,
    string Text,
    int Version,
    string Tier);

public sealed record SetSolutionContextRequest(string SolutionPath);

public sealed record AnalyzeResponse(
    string Uri,
    int Version,
    FunctionDto[] Functions,
    WarningDto[] Warnings);

public sealed record FunctionDto(
    string Id,
    string Name,
    string Kind,
    RangeDto Range,
    RangeDto SignatureRange,
    string Time,
    string Space,
    string Confidence,
    DimensionDto[] Dimensions,
    EvidenceDto Evidence,
    WarningDto[] Warnings,
    SuggestionDto[] BoundingSuggestions,
    string Tier);

public sealed record EvidenceDto(
    string Kind,
    string Label,
    string Cost,
    RangeDto? Range,
    EvidenceDto[] Children);

public sealed record DimensionDto(string Variable, string Meaning);

public sealed record WarningDto(string Message, RangeDto? Range);

public sealed record SuggestionDto(
    string Description,
    string Condition,
    string ResultingTime,
    string ResultingSpace);

public sealed record RangeDto(
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter);
