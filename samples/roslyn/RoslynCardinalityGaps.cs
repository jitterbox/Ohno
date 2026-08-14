using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace RoslynComplexityFixture;

/// <summary>
/// Cardinality and catalog gaps: refill worklists, SizeDelta,
/// heapify, sorted sets, builders, spans, and CFG reachability.
/// </summary>
public static class CardinalityGaps
{
    // Huffman: dequeue 2, enqueue 1. Time O(n log n), Space O(n).
    public static int Huffman(int[] freqs)
    {
        var heap = new PriorityQueue<int, int>();
        foreach (var f in freqs)
            heap.Enqueue(f, f);
        var cost = 0;
        while (heap.Count > 1)
        {
            var a = heap.Dequeue();
            var b = heap.Dequeue();
            var merged = a + b;
            cost += merged;
            heap.Enqueue(merged, merged);
        }

        return cost;
    }

    // Two heaps. Time O(n log n), Space O(n).
    public static void RunningMedian(int[] nums)
    {
        var low = new PriorityQueue<int, int>();
        var high = new PriorityQueue<int, int>();
        foreach (var x in nums)
        {
            low.Enqueue(x, -x);
            high.Enqueue(low.Dequeue(), 0);
            if (high.Count > low.Count)
                low.Enqueue(high.Dequeue(), 0);
        }
    }

    // Explicit stack DFS + visited. Time O(k n), Space O(n).
    public static int StackDepthFirstCount(
        List<int>[] graph, int start)
    {
        var visited = new bool[graph.Length];
        var stack = new Stack<int>();
        visited[start] = true;
        stack.Push(start);
        var count = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            count++;
            foreach (var next in graph[node])
            {
                if (visited[next]) continue;
                visited[next] = true;
                stack.Push(next);
            }
        }

        return count;
    }

    // Refill without a visit mark. NON_TERMINATION_RISK.
    public static int BfsNoVisited(List<int>[] graph, int start)
    {
        var queue = new Queue<int>();
        queue.Enqueue(start);
        var count = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            count++;
            foreach (var next in graph[node])
                queue.Enqueue(next);
        }

        return count;
    }

    // List window evicts with RemoveAt(0). Time O(k n), Space O(k).
    public static void WindowRemoveAt(int[] values, int k)
    {
        var window = new List<int>();
        foreach (var value in values)
        {
            window.Add(value);
            if (window.Count > k)
                window.RemoveAt(0);
        }
    }

    // Queue window evicts with TryDequeue. Time O(n), Space O(k).
    public static void WindowTryDequeue(int[] values, int k)
    {
        var window = new Queue<int>();
        foreach (var value in values)
        {
            window.Enqueue(value);
            if (window.Count > k)
                window.TryDequeue(out _);
        }
    }

    // Heapify from an enumerable. Time O(n), Space O(n).
    public static int HeapifyFromEnumerable(int[] values)
    {
        var pairs = new List<(int, int)>(values.Length);
        foreach (var v in values)
            pairs.Add((v, v));
        var heap = new PriorityQueue<int, int>(pairs);
        return heap.Count;
    }

    // SortedSet inserts. Time O(n log n), Space O(n).
    public static int SortedSetInsert(int[] values)
    {
        var set = new SortedSet<int>();
        foreach (var v in values)
            set.Add(v);
        return set.Count;
    }

    // StringBuilder append + ToString. Time O(n), Space O(n).
    public static string StringBuilderJoin(int n)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
            sb.Append(i);
        return sb.ToString();
    }

    // ImmutableList.Add is log n. Time O(n log n), Space O(n).
    public static ImmutableList<int> ImmutableListBuild(int[] values)
    {
        var list = ImmutableList<int>.Empty;
        foreach (var v in values)
            list = list.Add(v);
        return list;
    }

    // Span foreach. Time O(n), Space O(1).
    public static int SpanScan(Span<int> values)
    {
        var sum = 0;
        foreach (var v in values)
            sum += v;
        return sum;
    }

    // Collection spread. Time O(m + n), Space O(m + n).
    public static int[] CollectionSpread(int[] a, int[] b) =>
        [..a, ..b];

    // Halving via >>=. Time O(log n), Space O(1).
    public static int HalvingShift(int n)
    {
        var steps = 0;
        while (n > 0)
        {
            n >>= 1;
            steps++;
        }

        return steps;
    }

    // Dead Enqueue must not grow space. Time O(1), Space O(1).
    public static void UnreachableEnqueue(int n)
    {
        var q = new Queue<int>();
        if (false)
            q.Enqueue(n);
    }

    // Loop index i must not appear in Big-O. Time O(n²), Space O(1).
    public static int LoopIndexNotEmitted(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < i; j++)
                sum++;
        return sum;
    }
}
