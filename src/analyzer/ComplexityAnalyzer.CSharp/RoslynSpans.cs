using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

internal static class RoslynSpans
{
    public static LineSpan? Of(IOperation? operation)
    {
        if (operation?.Syntax is null) return null;
        return Of(operation.Syntax);
    }

    public static LineSpan? Of(SyntaxNode? node)
    {
        if (node is null) return null;
        var loc = node.GetLocation().GetLineSpan();
        if (!loc.IsValid) return null;
        var start = loc.StartLinePosition;
        var end = loc.EndLinePosition;
        return new LineSpan(
            start.Line, start.Character, end.Line, end.Character);
    }

    public static LineSpan? Of(SyntaxToken token)
    {
        var loc = token.GetLocation().GetLineSpan();
        if (!loc.IsValid) return null;
        var start = loc.StartLinePosition;
        var end = loc.EndLinePosition;
        return new LineSpan(
            start.Line, start.Character, end.Line, end.Character);
    }
}
