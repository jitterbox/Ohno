using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// Optional <c>MSBuildWorkspace</c> used by fast analysis when ready
/// and by the deep analysis tier.
/// Falls back to ad-hoc compilation when no solution is loaded or
/// MSBuild cannot be resolved.
/// </summary>
/// <remarks>
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.msbuild.msbuildworkspace">MSBuildWorkspace</see>
/// loads the real project graph (references, defines, language version).
/// Registration goes through <see cref="MsBuildBootstrap"/> /
/// <c>MSBuildLocator</c>. Edge case: a missing SDK or a project that
/// failed to load yields <see cref="LastError"/> and the ad-hoc
/// compilation, not a fabricated bound. Fast never waits on an
/// in-progress solution open; once ready it waits for the workspace
/// gate instead of falling back to ad-hoc.
/// </remarks>
public sealed class DeepWorkspace : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MSBuildWorkspace? _workspace;

    public string? SolutionPath { get; private set; }

    public string? LastError { get; private set; }

    public bool IsReady { get; private set; }

    public async Task SetSolutionAsync(
        string path, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            MsBuildBootstrap.Register();
            await OpenAfterLocatorAsync(path, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsReady = false;
            _workspace?.Dispose();
            _workspace = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SemanticModelLookup?> TryGetModelAsync(
        string filePath,
        string text,
        CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await ModelOfAsync(filePath, text, token)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SemanticModelLookup?> TryGetReadyModelAsync(
        string filePath,
        string text,
        CancellationToken token)
    {
        if (!IsReady) return null;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsReady) return null;
            return await ModelOfAsync(filePath, text, token)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _gate.Dispose();
    }

    private async Task OpenAfterLocatorAsync(
        string path, CancellationToken token)
    {
        var full = FilePaths.Normalize(path);
        SolutionPath = full;
        IsReady = false;
        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();
        if (full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            await _workspace.OpenSolutionAsync(full, cancellationToken: token);
        else
            await _workspace.OpenProjectAsync(full, cancellationToken: token);
        LastError = null;
        IsReady = true;
    }

    private async Task<SemanticModelLookup?> ModelOfAsync(
        string filePath,
        string text,
        CancellationToken token)
    {
        if (_workspace is null) return null;
        var document = _workspace.CurrentSolution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => FilePaths.Equal(d.FilePath, filePath));
        if (document is null) return null;
        if (text.Length > 0)
            document = document.WithText(SourceText.From(text));
        var model = await document.GetSemanticModelAsync(token)
            .ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(token)
            .ConfigureAwait(false);
        if (model is null || tree is null) return null;
        return new SemanticModelLookup(model, tree);
    }
}

public sealed record SemanticModelLookup(
    Microsoft.CodeAnalysis.SemanticModel Model,
    Microsoft.CodeAnalysis.SyntaxTree Tree);
