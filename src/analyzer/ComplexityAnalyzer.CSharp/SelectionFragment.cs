using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Picks the tightest statement-level operations that cover a
/// selection so the span can be analyzed as a method body.
/// </summary>
internal static class SelectionFragment
{
    public static IReadOnlyList<IOperation> Extract(
        IOperation? body, LineSpan span, SyntaxTree tree)
    {
        if (body is null) return [];
        if (!TryTextSpan(tree, span, out var selected)) return [];
        return From(body, selected);
    }

    private static IReadOnlyList<IOperation> From(
        IOperation root, TextSpan selected)
    {
        var block = DeepestBlock(root, selected) ?? AsBlock(root);
        if (block is null) return Cover(root, selected);
        var hits = block.Operations
            .Where(op => Intersects(op, selected))
            .ToArray();
        if (hits.Length == 0) return Cover(root, selected);
        if (hits.Length == block.Operations.Length)
            return [block];
        if (hits.Length == 1)
            return Tighten(hits[0], selected);
        return hits;
    }

    private static IReadOnlyList<IOperation> Tighten(
        IOperation operation, TextSpan selected)
    {
        var body = LoopBody(operation);
        if (body is null) return [operation];
        if (!Contains(body, selected) && !Intersects(body, selected))
            return [operation];
        if (!Contains(body, selected))
            return [operation];
        var nested = From(body, selected);
        return nested.Count > 0 ? nested : [operation];
    }

    private static IOperation? LoopBody(IOperation operation) =>
        operation switch
        {
            IForEachLoopOperation loop => loop.Body,
            IForLoopOperation loop => loop.Body,
            IWhileLoopOperation loop => loop.Body,
            _ => null,
        };

    public static IMethodSymbol? EnclosingMethod(
        SemanticModel model, SyntaxTree tree, LineSpan span)
    {
        if (!TryTextSpan(tree, span, out var selected)) return null;
        var root = tree.GetRoot();
        var token = root.FindToken(selected.Start);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var symbol = node switch
            {
                MethodDeclarationSyntax m => model.GetDeclaredSymbol(m),
                ConstructorDeclarationSyntax c =>
                    model.GetDeclaredSymbol(c),
                LocalFunctionStatementSyntax l =>
                    model.GetDeclaredSymbol(l),
                _ => null,
            };
            if (symbol is IMethodSymbol method) return method;
        }

        var unit = tree.GetRoot() as CompilationUnitSyntax;
        if (unit?.Members.OfType<GlobalStatementSyntax>().Any() == true)
            return model.Compilation.GetEntryPoint(CancellationToken.None);
        return null;
    }

    private static IReadOnlyList<IOperation> Cover(
        IOperation root, TextSpan selected)
    {
        var inner = Innermost(root, selected);
        return inner is null ? [] : [SnapStatement(inner)];
    }

    private static IBlockOperation? DeepestBlock(
        IOperation root, TextSpan selected)
    {
        var current = AsBlock(root) ?? FindAnyBlock(root, selected);
        while (current is not null)
        {
            var deeper = current.Operations
                .OfType<IBlockOperation>()
                .Cast<IBlockOperation?>()
                .FirstOrDefault(b => Contains(b!, selected));
            if (deeper is null) return current;
            current = deeper;
        }

        return current;
    }

    private static IBlockOperation? FindAnyBlock(
        IOperation root, TextSpan selected)
    {
        foreach (var op in Walk(root))
        {
            if (op is IBlockOperation block && Contains(block, selected))
                return block;
        }

        return null;
    }

    private static IBlockOperation? AsBlock(IOperation body) =>
        body as IBlockOperation;

    private static IOperation? Innermost(
        IOperation root, TextSpan selected)
    {
        if (!Contains(root, selected) && !Intersects(root, selected))
            return null;
        var best = root;
        var queue = new Queue<IOperation>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in current.ChildOperations)
            {
                if (!Contains(child, selected) && !Intersects(child, selected))
                    continue;
                queue.Enqueue(child);
                if (SpanOf(child).Length <= SpanOf(best).Length)
                    best = child;
            }
        }

        return best;
    }

    private static IOperation SnapStatement(IOperation operation)
    {
        for (var cur = operation; cur is not null; cur = cur.Parent)
        {
            if (cur.Parent is IBlockOperation or IMethodBodyOperation)
                return cur;
            if (cur is IForLoopOperation or IForEachLoopOperation
                or IWhileLoopOperation)
            {
                return cur;
            }
        }

        return operation;
    }

    private static bool TryTextSpan(
        SyntaxTree tree, LineSpan span, out TextSpan selected)
    {
        selected = default;
        var text = tree.GetText();
        if (text.Lines.Count == 0) return false;
        var startLine = Math.Clamp(span.StartLine, 0, text.Lines.Count - 1);
        var endLine = Math.Clamp(span.EndLine, 0, text.Lines.Count - 1);
        if (endLine < startLine) (startLine, endLine) = (endLine, startLine);
        var start = ClampChar(text.Lines[startLine], span.StartCharacter);
        var end = ClampChar(text.Lines[endLine], span.EndCharacter);
        if (end < start) (start, end) = (end, start);
        selected = TextSpan.FromBounds(start, end);
        return true;
    }

    private static int ClampChar(
        Microsoft.CodeAnalysis.Text.TextLine line, int character) =>
        line.Start + Math.Clamp(character, 0, line.Span.Length);

    private static bool Intersects(IOperation operation, TextSpan selected) =>
        SpanOf(operation).OverlapsWith(selected)
        || SpanOf(operation).Contains(selected);

    private static bool Contains(IOperation operation, TextSpan selected)
    {
        var span = SpanOf(operation);
        return span.Contains(selected)
            || (selected.Length == 0 && span.Contains(selected.Start));
    }

    private static TextSpan SpanOf(IOperation operation) =>
        operation.Syntax?.Span ?? default;

    private static IEnumerable<IOperation> Walk(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var nested in Walk(child))
                yield return nested;
        }
    }
}
