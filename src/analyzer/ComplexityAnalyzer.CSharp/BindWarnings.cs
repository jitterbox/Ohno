using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// File-level bind failures the ad-hoc compilation cannot see
/// (missing project types, file-based package directives).
/// </summary>
internal static class BindWarnings
{
    private static readonly HashSet<string> TypeErrors =
        ["CS0246", "CS0234"];

    public static IReadOnlyList<AnalysisWarning> For(SemanticModel model)
    {
        var tree = model.SyntaxTree;
        var source = tree.TryGetText(out var text)
            ? text.ToString()
            : string.Empty;
        var warnings = new List<AnalysisWarning>();
        AddFileBased(source, warnings);
        AddUnresolved(model, tree, warnings);
        return warnings;
    }

    private static void AddFileBased(
        string source, List<AnalysisWarning> warnings)
    {
        if (!HasFileDirective(source)) return;
        warnings.Add(new AnalysisWarning(
            "File-based app directives (#:package / #:sdk) are not "
            + "restored. Types from those packages will not bind."));
    }

    internal static bool HasFileDirective(string source)
    {
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var text = line.TrimStart();
            if (IsFileDirective(text)) return true;
        }

        return false;
    }

    private static bool IsFileDirective(string text) =>
        text.StartsWith("#:package", StringComparison.Ordinal)
        || text.StartsWith("#:project", StringComparison.Ordinal)
        || text.StartsWith("#:sdk", StringComparison.Ordinal)
        || text.StartsWith("#:property", StringComparison.Ordinal)
        || (text.StartsWith("#!", StringComparison.Ordinal)
            && text.Contains("dotnet", StringComparison.Ordinal));

    private static void AddUnresolved(
        SemanticModel model,
        SyntaxTree tree,
        List<AnalysisWarning> warnings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in model.GetDiagnostics())
        {
            if (warnings.Count >= 8) return;
            if (!IsTypeError(diagnostic, tree)) continue;
            var message = diagnostic.GetMessage();
            if (!seen.Add(message)) continue;
            warnings.Add(new AnalysisWarning(message, SpanOf(diagnostic)));
        }
    }

    private static bool IsTypeError(
        Diagnostic diagnostic, SyntaxTree tree)
    {
        if (diagnostic.Severity != DiagnosticSeverity.Error) return false;
        if (!TypeErrors.Contains(diagnostic.Id)) return false;
        return diagnostic.Location.SourceTree == tree;
    }

    private static LineSpan? SpanOf(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        if (!span.IsValid) return null;
        return new LineSpan(
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }
}
