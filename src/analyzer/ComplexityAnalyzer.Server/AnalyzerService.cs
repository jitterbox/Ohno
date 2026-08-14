using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using StreamJsonRpc;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// JSON-RPC service surface consumed by the VS Code extension.
/// </summary>
public sealed class AnalyzerService
{
    private readonly CSharpFileAnalyzer _file = new();
    private readonly DeepWorkspace _workspace = new();
    private readonly object _gate = new();
    private JsonRpc? _rpc;

    public void Attach(JsonRpc rpc) => _rpc = rpc;

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize() =>
        new("Ohno.ComplexityAnalyzer", "0.1.0");

    [JsonRpcMethod(
        "ohno/setSolutionContext",
        UseSingleObjectParameterDeserialization = true)]
    public void SetSolutionContext(SetSolutionContextRequest request)
    {
        lock (_gate)
        {
            try
            {
                _workspace.SetSolutionAsync(request.SolutionPath)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // LastError is recorded; analyzeDeep falls back to ad-hoc.
            }
        }
    }

    [JsonRpcMethod(
        "ohno/analyze",
        UseSingleObjectParameterDeserialization = true)]
    public AnalyzeResponse Analyze(AnalyzeRequest request)
    {
        return AnalyzeCore(request, AnalysisTier.Fast);
    }

    [JsonRpcMethod(
        "ohno/analyzeDeep",
        UseSingleObjectParameterDeserialization = true)]
    public AnalyzeResponse AnalyzeDeep(AnalyzeRequest request)
    {
        return AnalyzeCore(request, AnalysisTier.Deep);
    }

    [JsonRpcMethod("shutdown")]
    public void Shutdown()
    {
        _rpc?.Dispose();
    }

    private AnalyzeResponse AnalyzeCore(
        AnalyzeRequest request, AnalysisTier tier)
    {
        var analysis = AnalyzeWithWorkspace(request, tier);
        var functions = analysis.Functions
            .Select(f => MapFunction(f, tier))
            .ToArray();
        return new AnalyzeResponse(
            request.Uri,
            request.Version,
            functions,
            analysis.Warnings.Select(MapWarning).ToArray());
    }

    private FileAnalysis AnalyzeWithWorkspace(
        AnalyzeRequest request, AnalysisTier tier)
    {
        if (tier != AnalysisTier.Deep)
            return _file.Analyze(request.Text, tier);

        try
        {
            var lookup = _workspace.TryGetModel(FilePathOf(request.Uri));
            if (lookup is not null)
            {
                return _file.Analyze(
                    lookup.Model,
                    lookup.Tree,
                    tier,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            return Fallback(request, tier, ex.Message);
        }

        if (_workspace.LastError is { } error)
            return Fallback(request, tier, error);

        return _file.Analyze(request.Text, tier);
    }

    private FileAnalysis Fallback(
        AnalyzeRequest request, AnalysisTier tier, string reason)
    {
        var analysis = _file.Analyze(request.Text, tier);
        var warning = new AnalysisWarning(
            "Deep analysis unavailable; used ad-hoc compilation. "
            + reason);
        return analysis with
        {
            Warnings = analysis.Warnings.Append(warning).ToArray(),
        };
    }

    private static string FilePathOf(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.IsFile)
        {
            return parsed.LocalPath;
        }

        return uri;
    }

    private static FunctionDto MapFunction(
        AnalyzedFunction function, AnalysisTier tier)
    {
        var result = function.Result;
        return new FunctionDto(
            function.Symbol.ToDisplayString(),
            function.Symbol.Name,
            KindOf(function.Symbol.MethodKind),
            MapSpan(function.Range),
            MapSpan(function.SignatureRange),
            ComplexityFormatter.FormatBigO(result.Time),
            ComplexityFormatter.FormatBigO(result.AuxiliarySpace),
            result.Confidence.ToString().ToLowerInvariant(),
            result.Dimensions
                .Select(d => new DimensionDto(d.Variable, d.Meaning))
                .ToArray(),
            MapEvidence(result.Evidence),
            result.Warnings.Select(MapWarning).ToArray(),
            result.BoundingSuggestions.Select(MapSuggestion).ToArray(),
            tier == AnalysisTier.Deep ? "deep" : "fast");
    }

    private static EvidenceDto MapEvidence(ComplexityEvidence evidence) =>
        new(
            evidence.Kind,
            evidence.Label,
            ComplexityFormatter.Format(evidence.Cost),
            evidence.Span is null ? null : MapSpan(evidence.Span),
            evidence.Children.Select(MapEvidence).ToArray());

    private static WarningDto MapWarning(AnalysisWarning warning) =>
        new(warning.Message, warning.Span is null ? null : MapSpan(warning.Span));

    private static SuggestionDto MapSuggestion(BoundingSuggestion s) =>
        new(
            s.Description,
            s.Condition,
            ComplexityFormatter.FormatBigO(s.ResultingTime),
            ComplexityFormatter.FormatBigO(s.ResultingSpace));

    private static RangeDto MapSpan(LineSpan span) =>
        new(span.StartLine, span.StartCharacter, span.EndLine, span.EndCharacter);

    private static string KindOf(Microsoft.CodeAnalysis.MethodKind kind) =>
        kind switch
        {
            Microsoft.CodeAnalysis.MethodKind.Constructor => "constructor",
            Microsoft.CodeAnalysis.MethodKind.LocalFunction => "localFunction",
            Microsoft.CodeAnalysis.MethodKind.AnonymousFunction => "lambda",
            Microsoft.CodeAnalysis.MethodKind.UserDefinedOperator => "operator",
            _ => "method",
        };
}
