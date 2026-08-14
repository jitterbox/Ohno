using Microsoft.CodeAnalysis.MSBuild;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// Optional MSBuild workspace used by the deep analysis tier.
/// Falls back to ad-hoc compilation when no solution is loaded or
/// MSBuild cannot be resolved.
/// </summary>
public sealed class DeepWorkspace : IDisposable
{
    private MSBuildWorkspace? _workspace;

    public string? SolutionPath { get; private set; }

    public string? LastError { get; private set; }

    public async Task SetSolutionAsync(string path)
    {
        try
        {
            MsBuildBootstrap.Register();
            await OpenAfterLocatorAsync(path);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _workspace?.Dispose();
            _workspace = null;
            throw;
        }
    }

    public SemanticModelLookup? TryGetModel(string filePath)
    {
        if (_workspace is null) return null;
        var document = _workspace.CurrentSolution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d =>
                string.Equals(
                    d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (document is null) return null;
        var model = document.GetSemanticModelAsync().GetAwaiter().GetResult();
        var tree = document.GetSyntaxTreeAsync().GetAwaiter().GetResult();
        if (model is null || tree is null) return null;
        return new SemanticModelLookup(model, tree);
    }

    public void Dispose() => _workspace?.Dispose();

    private async Task OpenAfterLocatorAsync(string path)
    {
        SolutionPath = path;
        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();
        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            await _workspace.OpenSolutionAsync(path);
        else
            await _workspace.OpenProjectAsync(path);
        LastError = null;
    }
}

public sealed record SemanticModelLookup(
    Microsoft.CodeAnalysis.SemanticModel Model,
    Microsoft.CodeAnalysis.SyntaxTree Tree);
