using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using StreamJsonRpc;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// JSON-RPC service surface consumed by the VS Code extension.
/// Methods that touch MSBuild or Roslyn are async so the dispatcher
/// is not blocked and the client can cancel in-flight work.
/// </summary>
public sealed class AnalyzerService
{
    private readonly CSharpFileAnalyzer _file = new();
    private readonly DeepWorkspace _workspace = new();
    private readonly object _solutionGate = new();
    private Task? _solutionTask;
    private CancellationTokenSource? _fastCts;
    private JsonRpc? _rpc;

    public void Attach(JsonRpc rpc) => _rpc = rpc;

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize() =>
        new("Ohno.ComplexityAnalyzer", "0.1.0");

    [JsonRpcMethod(
        "ohno/setSolutionContext",
        UseSingleObjectParameterDeserialization = true)]
    public Task SetSolutionContext(
        SetSolutionContextRequest request,
        CancellationToken token = default)
    {
        var open = StartSolutionOpen(request.SolutionPath);
        return AwaitSolution(open, token);
    }

    [JsonRpcMethod(
        "ohno/analyze",
        UseSingleObjectParameterDeserialization = true)]
    public Task<AnalyzeResponse> Analyze(
        AnalyzeRequest request,
        CancellationToken token = default) =>
        AnalyzeAsync(request, AnalysisTier.Fast, token);

    [JsonRpcMethod(
        "ohno/analyzeDeep",
        UseSingleObjectParameterDeserialization = true)]
    public Task<AnalyzeResponse> AnalyzeDeep(
        AnalyzeRequest request,
        CancellationToken token = default) =>
        AnalyzeAsync(request, AnalysisTier.Deep, token);

    [JsonRpcMethod("shutdown")]
    public void Shutdown()
    {
        _rpc?.Dispose();
    }

    private async Task<AnalyzeResponse> AnalyzeAsync(
        AnalyzeRequest request,
        AnalysisTier tier,
        CancellationToken token)
    {
        using var linked = LinkFastCancel(tier, token);
        try
        {
            var analysis = await AnalyzeFileAsync(
                request, tier, linked.Token).ConfigureAwait(false);
            var functions = analysis.Functions
                .Select(f => MapFunction(f, tier, analysis.Warnings))
                .ToArray();
            return new AnalyzeResponse(
                request.Uri,
                request.Version,
                functions,
                analysis.Warnings.Select(MapWarning).ToArray());
        }
        finally
        {
            Interlocked.CompareExchange(ref _fastCts, null, linked);
        }
    }

    private async Task<FileAnalysis> AnalyzeFileAsync(
        AnalyzeRequest request,
        AnalysisTier tier,
        CancellationToken token)
    {
        if (tier == AnalysisTier.Deep)
        {
            return await AnalyzeDeepFileAsync(request, token)
                .ConfigureAwait(false);
        }

        return await AnalyzeFastFileAsync(request, token)
            .ConfigureAwait(false);
    }

    private async Task<FileAnalysis> AnalyzeFastFileAsync(
        AnalyzeRequest request,
        CancellationToken token)
    {
        var lookup = await _workspace.TryGetReadyModelAsync(
            FilePaths.FromUri(request.Uri), request.Text, token)
            .ConfigureAwait(false);
        if (lookup is null)
        {
            return await Task.Run(
                () => _file.Analyze(
                    request.Text, AnalysisTier.Fast, token),
                token).ConfigureAwait(false);
        }

        return await Task.Run(
            () => _file.Analyze(
                lookup.Model, lookup.Tree,
                AnalysisTier.Fast, token),
            token).ConfigureAwait(false);
    }

    private async Task<FileAnalysis> AnalyzeDeepFileAsync(
        AnalyzeRequest request,
        CancellationToken token)
    {
        await AwaitCurrentSolution(token).ConfigureAwait(false);
        try
        {
            var lookup = await _workspace.TryGetModelAsync(
                FilePaths.FromUri(request.Uri), request.Text, token)
                .ConfigureAwait(false);
            if (lookup is not null)
            {
                return await Task.Run(
                    () => _file.Analyze(
                        lookup.Model, lookup.Tree,
                        AnalysisTier.Deep, token),
                    token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fallback(request, ex.Message, token);
        }

        if (_workspace.LastError is { } error)
            return Fallback(request, error, token);

        if (_workspace.SolutionPath is { } solution)
        {
            return Fallback(
                request,
                $"Document is not part of {solution}.",
                token);
        }

        return await Task.Run(
            () => _file.Analyze(
                request.Text, AnalysisTier.Deep, token),
            token).ConfigureAwait(false);
    }

    private FileAnalysis Fallback(
        AnalyzeRequest request, string reason, CancellationToken token)
    {
        var analysis = _file.Analyze(
            request.Text, AnalysisTier.Deep, token);
        var warning = new AnalysisWarning(
            "Deep analysis unavailable; used ad-hoc compilation. "
            + reason);
        return analysis with
        {
            Warnings = analysis.Warnings.Append(warning).ToArray(),
        };
    }

    private Task StartSolutionOpen(string path)
    {
        lock (_solutionGate)
        {
            var open = _workspace.SetSolutionAsync(path);
            _solutionTask = open;
            return open;
        }
    }

    private async Task AwaitCurrentSolution(CancellationToken token)
    {
        Task? open;
        lock (_solutionGate) open = _solutionTask;
        if (open is null) return;
        await AwaitSolution(open, token).ConfigureAwait(false);
    }

    private static async Task AwaitSolution(
        Task open, CancellationToken token)
    {
        try
        {
            await open.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // LastError is recorded; analyzeDeep falls back to ad-hoc.
        }
    }

    private CancellationTokenSource LinkFastCancel(
        AnalysisTier tier, CancellationToken token)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (tier == AnalysisTier.Fast)
            TryCancel(Interlocked.Exchange(ref _fastCts, linked));
        return linked;
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static FunctionDto MapFunction(
        AnalyzedFunction function,
        AnalysisTier tier,
        IReadOnlyList<AnalysisWarning> fileWarnings)
    {
        var result = function.Result;
        return new FunctionDto(
            function.Symbol.ToDisplayString(),
            DisplayName(function.Symbol),
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
            result.Warnings.Concat(fileWarnings)
                .Select(MapWarning)
                .ToArray(),
            result.BoundingSuggestions.Select(MapSuggestion).ToArray(),
            result.Explanation,
            result.Patterns.Select(p =>
                new PatternDto(p.Id, p.Label, p.Reason)).ToArray(),
            result.ConfidenceReasons.ToArray(),
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

    private static string DisplayName(
        Microsoft.CodeAnalysis.IMethodSymbol symbol) =>
        symbol.Name is "<Main>$" ? "Main" : symbol.Name;

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
