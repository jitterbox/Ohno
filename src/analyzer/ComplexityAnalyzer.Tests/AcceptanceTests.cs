using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Xunit;

namespace ComplexityAnalyzer.Tests;

public class AcceptanceTests
{
    private static (string Time, string Space, AnalysisConfidence Confidence)
        Run(string source)
    {
        var result = SnippetAnalyzer.Analyze(source);
        return (
            ComplexityFormatter.FormatBigO(result.Time),
            ComplexityFormatter.FormatBigO(result.AuxiliarySpace),
            result.Confidence);
    }

    private static IEnumerable<string> Labels(ComplexityEvidence node)
    {
        yield return node.Label;
        foreach (var child in node.Children)
        {
            foreach (var label in Labels(child))
                yield return label;
        }
    }

    [Fact]
    public void Constant_IndexAccess()
    {
        var (time, space, _) = Run("""
            static int GetFirst(int[] nums) => nums[0];
            """);
        Assert.Equal("O(1)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void LinearScan()
    {
        var (time, space, _) = Run("""
            static bool Contains(int[] nums, int value)
            {
                foreach (var n in nums)
                    if (n == value)
                        return true;
                return false;
            }
            """);
        Assert.Equal("O(n)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void NestedIndependentLoops()
    {
        var (time, space, _) = Run("""
            static void Nested(int[] first, int[] second)
            {
                foreach (var a in first)
                    foreach (var b in second)
                        Use(a, b);
            }

            static void Use(int a, int b) { }
            """);
        Assert.Equal("O(m n)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void TriangularLoop()
    {
        var (time, space, _) = Run("""
            static void Triangle(int n)
            {
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < i; j++)
                        Use(i, j);
            }

            static void Use(int a, int b) { }
            """);
        Assert.Equal("O(n²)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void LogarithmicLoop()
    {
        var (time, space, _) = Run("""
            static void LogWalk(int n)
            {
                for (int i = 1; i < n; i *= 2)
                    Use(i);
            }

            static void Use(int i) { }
            """);
        Assert.Equal("O(log n)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void ArraySort()
    {
        var (time, _, _) = Run("""
            static void SortNums(int[] nums) => Array.Sort(nums);
            """);
        Assert.Equal("O(n log n)", time);
    }

    [Fact]
    public void DictionaryLookupLoop()
    {
        var (time, space, confidence) = Run("""
            static void Lookup(
                string[] keys, Dictionary<string, int> dictionary)
            {
                foreach (var key in keys)
                    dictionary.TryGetValue(key, out _);
            }
            """);
        Assert.Equal("O(n)", time);
        Assert.Equal("O(1)", space);
        Assert.True(confidence >= AnalysisConfidence.Medium);
    }

    [Fact]
    public void DictionaryIndexerWriteLoop()
    {
        var (time, space, _) = Run("""
            static void Index(int[] nums)
            {
                var map = new Dictionary<int, int>();
                for (var i = 0; i < nums.Length; i++)
                    map[nums[i]] = i;
            }
            """);
        Assert.Equal("O(n)", time);
        Assert.Equal("O(n)", space);
    }

    [Fact]
    public void RectangularArrayIsQuadraticSpace()
    {
        var (time, space, _) = Run("""
            static int[,] CreateMatrix(int n) => new int[n, n];
            """);
        Assert.Equal("O(n²)", time);
        Assert.Equal("O(n²)", space);
    }

    [Fact]
    public void ImplicitArrayLiteralIsConstantSpace()
    {
        var (time, space, _) = Run("""
            static int[] Pair(int a, int b) => new[] { a, b };
            """);
        Assert.Equal("O(1)", time);
        Assert.Equal("O(1)", space);
    }

    [Fact]
    public void MergeKLists_HeapWorklist()
    {
        var result = SnippetAnalyzer.AnalyzeNamed("""
            using System.Collections.Generic;

            public class Solution
            {
                public ListNode MergeKLists(ListNode[] lists)
                {
                    var heap = new PriorityQueue<ListNode, int>();
                    foreach (var node in lists)
                    {
                        if (node != null)
                            heap.Enqueue(node, node.val);
                    }
                    var dummy = new ListNode();
                    var tail = dummy;
                    while (heap.Count > 0)
                    {
                        var node = heap.Dequeue();
                        tail.next = node;
                        tail = node;
                        if (node.next != null)
                            heap.Enqueue(node.next, node.next.val);
                    }
                    return dummy.next;
                }
            }

            public class ListNode
            {
                public int val;
                public ListNode next;
                public ListNode(int val = 0, ListNode next = null)
                {
                    this.val = val;
                    this.next = next;
                }
            }
            """, name: "MergeKLists");
        var time = ComplexityFormatter.FormatBigO(result.Time);
        var space = ComplexityFormatter.FormatBigO(
            result.AuxiliarySpace);
        Assert.Equal("O(n log k)", time);
        Assert.Equal("O(k)", space);
        Assert.Contains(result.Dimensions, d => d.Variable == "k");
        Assert.Contains(result.Dimensions, d => d.Variable == "n");
    }

    [Fact]
    public void MergeKLists_WithoutUsings_StillBindsHeap()
    {
        var source = """
            public class Solution
            {
                public ListNode MergeKLists(ListNode[] lists)
                {
                    var heap = new PriorityQueue<ListNode, int>();
                    foreach (var node in lists)
                    {
                        if (node != null)
                            heap.Enqueue(node, node.val);
                    }
                    var dummy = new ListNode();
                    var tail = dummy;
                    while (heap.Count > 0)
                    {
                        var node = heap.Dequeue();
                        tail.next = node;
                        tail = node;
                        if (node.next != null)
                            heap.Enqueue(node.next, node.next.val);
                    }
                    return dummy.next;
                }
            }

            public class ListNode
            {
                public int val;
                public ListNode next;
                public ListNode(int val = 0, ListNode next = null)
                {
                    this.val = val;
                    this.next = next;
                }
            }
            """;
        var analysis = new CSharpFileAnalyzer()
            .Analyze(source, AnalysisTier.Fast);
        var result = analysis.Functions
            .Single(f => f.Symbol.Name == "MergeKLists")
            .Result;
        Assert.Equal(
            "O(n log k)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal(
            "O(k)", ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
    }

    [Fact]
    public void TopKBoundedHeap()
    {
        var result = SnippetAnalyzer.Analyze("""
            static int[] TopK(int[] values, int k)
            {
                var pq = new PriorityQueue<int, int>();
                foreach (var value in values)
                {
                    pq.Enqueue(value, value);
                    if (pq.Count > k)
                        pq.Dequeue();
                }
                return pq.UnorderedItems.Select(x => x.Element).ToArray();
            }
            """);
        Assert.Equal("O(n log k)", ComplexityFormatter.FormatBigO(result.Time));
        Assert.Equal("O(k)", ComplexityFormatter.FormatBigO(result.AuxiliarySpace));
        Assert.Contains(result.Dimensions, d => d.Variable == "n");
        Assert.Contains(result.Dimensions, d => d.Variable == "k");
        Assert.True(result.Confidence >= AnalysisConfidence.Medium);
        Assert.DoesNotContain(
            Labels(result.Evidence),
            label => label == "empty");
    }

    [Fact]
    public void LinqLinearPipeline()
    {
        var (time, space, _) = Run("""
            static int[] Scale(int[] values) =>
                values.Where(x => x > 0).Select(x => x * 2).ToArray();
            """);
        Assert.Equal("O(n)", time);
        Assert.Equal("O(n)", space);
    }

    [Fact]
    public void LinqSort()
    {
        var (time, space, _) = Run("""
            static int[] Sorted(int[] values) =>
                values.OrderBy(x => x).ToArray();
            """);
        Assert.Equal("O(n log n)", time);
        Assert.Equal("O(n)", space);
    }

    [Fact]
    public void Interprocedural()
    {
        var result = SnippetAnalyzer.AnalyzeNamed("""
            using System;
            public static class Snippet
            {
                public static void Foo(int[] nums)
                {
                    foreach (var n in nums)
                        Bar(n);
                }

                public static void Bar(int value)
                {
                    Console.WriteLine(value);
                }
            }
            """, name: "Foo");
        Assert.Equal("O(n)", ComplexityFormatter.FormatBigO(result.Time));
    }

    [Fact]
    public void UnknownCall_RemainsVisible()
    {
        var result = SnippetAnalyzer.Analyze("""
            static void Walk(int[] items, IProcessor externalThing)
            {
                foreach (var item in items)
                    externalThing.Process(item);
            }

            public interface IProcessor { void Process(int item); }
            """);
        var time = ComplexityFormatter.Format(result.Time);
        Assert.Contains("C(Process)", time);
        Assert.Contains("n", time);
        Assert.True(result.Confidence <= AnalysisConfidence.Low);
    }
}
