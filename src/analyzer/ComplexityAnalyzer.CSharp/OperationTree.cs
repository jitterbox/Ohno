using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Shared <see cref="IOperation"/> traversal.
/// </summary>
/// <remarks>
/// Seven detectors each carried their own recursive iterator. Nested
/// <c>yield return</c> recursion chains one enumerator per level, so a
/// tree of N nodes at depth D costs O(N·D) MoveNext hops before any
/// detector does work.
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.operations.operationextensions.descendants">Descendants()</see>
/// is Roslyn's own iterative walk over the same nodes in the same
/// pre-order, so this is one implementation instead of seven and it
/// does not pay the enumerator chain.
/// </remarks>
internal static class OperationTree
{
    /// <summary>
    /// The operation and every descendant, pre-order. Matches the
    /// hand-rolled walkers this replaces, which yielded the root first.
    /// </summary>
    public static IEnumerable<IOperation> SelfAndDescendants(
        IOperation root)
    {
        yield return root;
        foreach (var descendant in root.Descendants())
            yield return descendant;
    }

    /// <summary>
    /// Every descendant, pre-order, excluding the operation itself.
    /// </summary>
    public static IEnumerable<IOperation> Descendants(IOperation root) =>
        root.Descendants();

    /// <summary>
    /// The operation and its descendants, but not across a nested loop
    /// boundary. Used where a detector asks "does this loop body do X
    /// at its own level", so an inner loop's contents must not count.
    /// </summary>
    public static IEnumerable<IOperation> WithinLoopLevel(IOperation root)
    {
        var stack = new Stack<IOperation>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            if (IsLoop(current)) continue;

            // Pushed in reverse so siblings pop in source order, which
            // keeps this pre-order like the recursion it replaces.
            var children = current.ChildOperations.ToArray();
            for (var i = children.Length - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
    }

    private static bool IsLoop(IOperation operation) =>
        operation is IForLoopOperation
            or IForEachLoopOperation
            or IWhileLoopOperation;
}
