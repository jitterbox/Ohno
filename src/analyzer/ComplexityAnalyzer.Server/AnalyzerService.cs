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
    private readonly CancelSlot _documentRuns = new();
    private readonly CancelSlot _selectionRuns = new();
    private JsonRpc? _rpc;

    public void Attach(JsonRpc rpc) => _rpc = rpc;

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize() =>
        new("Ohno.ComplexityAnalyzer", "0.1.2");

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
        var slot = SlotFor(tier, request);
        using var linked = slot?.Replace(token)
            ?? CancellationTokenSource.CreateLinkedTokenSource(token);
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
            slot?.Release(linked);
        }
    }

    /// <summary>
    /// Superseding only applies within a kind. Document and selection
    /// analysis are both Fast and both arrive on <c>ohno/analyze</c>,
    /// so a single slot made them cancel each other: an edit with an
    /// active selection scheduled both, the document request landed
    /// second, and the selection result was dropped every time.
    /// Deep runs are user-initiated and are never superseded.
    /// </summary>
    private CancelSlot? SlotFor(AnalysisTier tier, AnalyzeRequest request)
    {
        if (tier != AnalysisTier.Fast) return null;
        return request.Selection is null ? _documentRuns : _selectionRuns;
    }

    /// <summary>
    /// Holds the one in-flight request of a kind, cancelling the
    /// previous one when a newer request supersedes it.
    /// </summary>
    private sealed class CancelSlot
    {
        private CancellationTokenSource? _current;

        public CancellationTokenSource Replace(CancellationToken token)
        {
            var linked =
                CancellationTokenSource.CreateLinkedTokenSource(token);
            TryCancel(Interlocked.Exchange(ref _current, linked));
            return linked;
        }

        public void Release(CancellationTokenSource source) =>
            Interlocked.CompareExchange(ref _current, null, source);

        private static void TryCancel(CancellationTokenSource? source)
        {
            if (source is null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
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
                () => RunFile(request, null, AnalysisTier.Fast, token),
                token).ConfigureAwait(false);
        }

        return await Task.Run(
            () => RunFile(request, lookup, AnalysisTier.Fast, token),
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
                    () => RunFile(
                        request, lookup, AnalysisTier.Deep, token),
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
            () => RunFile(request, null, AnalysisTier.Deep, token),
            token).ConfigureAwait(false);
    }

    private FileAnalysis RunFile(
        AnalyzeRequest request,
        SemanticModelLookup? lookup,
        AnalysisTier tier,
        CancellationToken token)
    {
        if (request.Selection is { } sel)
        {
            var span = Unmap(sel);
            if (lookup is null)
                return _file.AnalyzeSelection(
                    request.Text, span, tier, token);
            return _file.AnalyzeSelection(
                lookup.Model, lookup.Tree, span, tier, token);
        }

        if (lookup is null)
            return _file.Analyze(request.Text, tier, token);
        return _file.Analyze(
            lookup.Model, lookup.Tree, tier, token);
    }

    private static LineSpan Unmap(RangeDto range) =>
        new(range.StartLine, range.StartCharacter,
            range.EndLine, range.EndCharacter);

    private FileAnalysis Fallback(
        AnalyzeRequest request, string reason, CancellationToken token)
    {
        var analysis = RunFile(
            request, null, AnalysisTier.Deep, token);
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


    private static FunctionDto MapFunction(
        AnalyzedFunction function,
        AnalysisTier tier,
        IReadOnlyList<AnalysisWarning> fileWarnings)
    {
        var result = function.Result;
        return new FunctionDto(
            function.IsSelection
                ? function.Symbol.ToDisplayString() + "#selection"
                : function.Symbol.ToDisplayString(),
            function.IsSelection
                ? DisplayName(function.Symbol) + " (selection)"
                : DisplayName(function.Symbol),
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
            result.Patterns.Select(MapPattern).ToArray(),
            result.ConfidenceReasons.ToArray(),
            result.Approaches.Select(MapApproach).ToArray(),
            result.SelectionHint,
            tier == AnalysisTier.Deep ? "deep" : "fast");
    }

    private static PatternDto MapPattern(RecognizedPattern pattern) =>
        new(
            pattern.Id,
            pattern.Label,
            pattern.Reason,
            pattern.Effect.ToString().ToLowerInvariant(),
            pattern.Range is null ? null : MapSpan(pattern.Range));

    private static ApproachDto MapApproach(AlgorithmApproach approach) =>
        new(
            approach.Id,
            approach.Name,
            approach.Summary,
            approach.Role,
            approach.TimeHint);

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

    /// <summary>
    /// A name a reader recognizes. Accessors are metadata-named
    /// <c>get_Foo</c> / <c>set_Foo</c>, and indexers are <c>this[]</c>;
    /// both read better as the member plus the accessor.
    /// </summary>
    private static string DisplayName(
        Microsoft.CodeAnalysis.IMethodSymbol symbol)
    {
        if (symbol.Name is "<Main>$") return "Main";
        if (symbol.AssociatedSymbol is { } member)
        {
            var accessor = symbol.MethodKind switch
            {
                Microsoft.CodeAnalysis.MethodKind.PropertyGet => ".get",
                Microsoft.CodeAnalysis.MethodKind.PropertySet => ".set",
                _ => string.Empty,
            };
            return member.Name + accessor;
        }

        return symbol.Name;
    }

    private static string KindOf(Microsoft.CodeAnalysis.MethodKind kind) =>
        kind switch
        {
            Microsoft.CodeAnalysis.MethodKind.Constructor => "constructor",
            Microsoft.CodeAnalysis.MethodKind.LocalFunction => "localFunction",
            Microsoft.CodeAnalysis.MethodKind.AnonymousFunction => "lambda",
            Microsoft.CodeAnalysis.MethodKind.UserDefinedOperator
                or Microsoft.CodeAnalysis.MethodKind.Conversion => "operator",
            Microsoft.CodeAnalysis.MethodKind.PropertyGet
                or Microsoft.CodeAnalysis.MethodKind.PropertySet =>
                "property",
            _ => "method",
        };
}
