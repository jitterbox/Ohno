using System;
using System.Collections.Generic;

namespace ComplexityFixtures;

/// <summary>
/// Roslyn/static-analysis fixture for recognizable space-complexity patterns.
///
/// Important modeling rule:
/// Space complexity here means peak simultaneously retained memory, not cumulative
/// allocations over the lifetime of the method.
///
/// Unless otherwise stated, "Space" means auxiliary space. Output space is called
/// out separately when the returned value itself dominates memory usage.
/// </summary>
public static class RoslynSpaceComplexityPatterns
{
    // -------------------------------------------------------------------------
    // 1. CONSTANT SPACE
    //
    // Known Space: Θ(1)
    // Reason:
    //   A fixed number of scalar locals is retained regardless of n.
    // -------------------------------------------------------------------------
    public static int ConstantSpace(int[] values)
    {
        int sum = 0;
        int max = int.MinValue;

        foreach (int value in values)
        {
            sum += value;
            if (value > max)
                max = value;
        }

        return sum + max;
    }


    // -------------------------------------------------------------------------
    // 2. SINGLE LINEAR ALLOCATION
    //
    // Known Auxiliary Space: Θ(n)
    // Reason:
    //   One int[n] array is retained.
    // -------------------------------------------------------------------------
    public static void LinearArray(int n)
    {
        var buffer = new int[n];
        Consume(buffer);
    }


    // -------------------------------------------------------------------------
    // 3. TWO INDEPENDENT INPUT DIMENSIONS
    //
    // Known Auxiliary Space: Θ(m + n)
    // Reason:
    //   Both arrays are simultaneously live.
    // -------------------------------------------------------------------------
    public static void TwoIndependentArrays(int m, int n)
    {
        var left = new int[m];
        var right = new int[n];

        Consume(left);
        Consume(right);
    }


    // -------------------------------------------------------------------------
    // 4. RECTANGULAR MATRIX
    //
    // Known Auxiliary Space: Θ(mn)
    // Reason:
    //   The rectangular array contains m * n elements.
    // -------------------------------------------------------------------------
    public static void RectangularMatrix(int m, int n)
    {
        var matrix = new int[m, n];
        Consume(matrix);
    }


    // -------------------------------------------------------------------------
    // 5. SQUARE MATRIX
    //
    // Known Auxiliary Space: Θ(n²)
    // Reason:
    //   The array contains n * n elements.
    // -------------------------------------------------------------------------
    public static void SquareMatrix(int n)
    {
        var matrix = new int[n, n];
        Consume(matrix);
    }


    // -------------------------------------------------------------------------
    // 6. CUBIC ALLOCATION
    //
    // Known Auxiliary Space: Θ(n³)
    // Reason:
    //   The array contains n * n * n elements.
    // -------------------------------------------------------------------------
    public static void CubicArray(int n)
    {
        var cube = new int[n, n, n];
        Consume(cube);
    }


    // -------------------------------------------------------------------------
    // 7. REPEATED ALLOCATION WITHOUT RETENTION
    //
    // Known Peak Auxiliary Space: Θ(n)
    // NOT Θ(n²)
    //
    // Reason:
    //   Each iteration allocates int[n], but only one buffer is live at a time
    //   assuming Consume does not retain the reference.
    //
    // Analyzer trap:
    //   Counting cumulative allocation volume instead of peak retained memory.
    // -------------------------------------------------------------------------
    public static void RepeatedButNotRetained(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var buffer = new int[n];
            Consume(buffer);
        }
    }


    // -------------------------------------------------------------------------
    // 8. REPEATED ALLOCATION WITH RETENTION
    //
    // Known Auxiliary Space: Θ(n²)
    // Reason:
    //   n arrays are retained, and each array contains n elements.
    //
    //   n * n = n²
    // -------------------------------------------------------------------------
    public static void RepeatedAndRetained(int n)
    {
        var buffers = new List<int[]>(n);

        for (int i = 0; i < n; i++)
        {
            buffers.Add(new int[n]);
        }

        Consume(buffers);
    }


    // -------------------------------------------------------------------------
    // 9. BOUNDED TOP-K HEAP
    //
    // Known Auxiliary Space: Θ(k)
    // Reason:
    //   The priority queue is explicitly bounded to at most k elements.
    //
    // Assumption:
    //   k is an independent input parameter and k <= n.
    // -------------------------------------------------------------------------
    public static void TopK(int[] values, int k)
    {
        var heap = new PriorityQueue<int, int>();

        foreach (int value in values)
        {
            heap.Enqueue(value, value);

            if (heap.Count > k)
                heap.Dequeue();
        }

        Consume(heap);
    }


    // -------------------------------------------------------------------------
    // 10. SLIDING WINDOW
    //
    // Known Auxiliary Space: Θ(k)
    // Reason:
    //   The queue retains at most k elements at any moment.
    // -------------------------------------------------------------------------
    public static void SlidingWindow(int[] values, int k)
    {
        var window = new Queue<int>(k);

        foreach (int value in values)
        {
            window.Enqueue(value);

            if (window.Count > k)
                window.Dequeue();
        }

        Consume(window);
    }


    // -------------------------------------------------------------------------
    // 11. UNIQUE-VALUE SET
    //
    // Known Auxiliary Space: Θ(u)
    // where:
    //   u = number of distinct input values
    //
    // Worst case:
    //   u = n, therefore O(n)
    //
    // Reason:
    //   Space depends on cardinality, not merely iteration count.
    // -------------------------------------------------------------------------
    public static int CountUnique(int[] values)
    {
        var seen = new HashSet<int>();

        foreach (int value in values)
            seen.Add(value);

        return seen.Count;
    }


    // -------------------------------------------------------------------------
    // 12. GRAPH ADJACENCY LIST
    //
    // Known Output Space: Θ(V + E)
    // Reason:
    //   V lists are retained plus one stored adjacency entry per directed edge.
    //
    // Note:
    //   If undirected edges are stored in both directions, storage is
    //   V + 2E, which simplifies to Θ(V + E).
    // -------------------------------------------------------------------------
    public static List<int>[] BuildAdjacencyList(
        int vertexCount,
        IEnumerable<(int From, int To)> edges)
    {
        var graph = new List<int>[vertexCount];

        for (int i = 0; i < vertexCount; i++)
            graph[i] = new List<int>();

        foreach (var (from, to) in edges)
            graph[from].Add(to);

        return graph;
    }


    // -------------------------------------------------------------------------
    // 13. DENSE GRAPH ADJACENCY MATRIX
    //
    // Known Output Space: Θ(V²)
    // Reason:
    //   Every possible pair of vertices has a matrix slot.
    // -------------------------------------------------------------------------
    public static bool[,] BuildAdjacencyMatrix(int vertexCount)
    {
        return new bool[vertexCount, vertexCount];
    }


    // -------------------------------------------------------------------------
    // 14. RECURSION THAT HALVES THE PROBLEM
    //
    // Known Auxiliary Stack Space: Θ(log n)
    // Reason:
    //   Each recursive call halves the remaining problem size.
    //
    // Recurrence for depth:
    //   D(n) = D(n / 2) + 1
    // -------------------------------------------------------------------------
    public static int RecursiveBinarySearch(
        int[] values,
        int target,
        int left,
        int right)
    {
        if (left > right)
            return -1;

        int mid = left + (right - left) / 2;

        if (values[mid] == target)
            return mid;

        return values[mid] < target
            ? RecursiveBinarySearch(values, target, mid + 1, right)
            : RecursiveBinarySearch(values, target, left, mid - 1);
    }


    // -------------------------------------------------------------------------
    // 15. LINEAR RECURSION DEPTH
    //
    // Known Auxiliary Stack Space: Θ(n)
    // Reason:
    //   Each call reduces n by exactly one, so n stack frames can coexist.
    // -------------------------------------------------------------------------
    public static long LinearRecursion(int n)
    {
        if (n <= 0)
            return 0;

        return n + LinearRecursion(n - 1);
    }


    // -------------------------------------------------------------------------
    // 16. EXPONENTIAL TIME DOES NOT IMPLY EXPONENTIAL STACK SPACE
    //
    // Known Auxiliary Stack Space: Θ(n)
    // Time: Θ(2^n)
    //
    // Reason:
    //   The two recursive branches execute sequentially.
    //   Maximum simultaneously active depth is still only n.
    //
    // Analyzer trap:
    //   Confusing recursion-tree node count with maximum live recursion depth.
    // -------------------------------------------------------------------------
    public static long FibonacciNaive(int n)
    {
        if (n <= 1)
            return n;

        return FibonacciNaive(n - 1) + FibonacciNaive(n - 2);
    }


    // -------------------------------------------------------------------------
    // 17. MEMOIZED 2D STATE SPACE
    //
    // Known Auxiliary Space: Θ(mn)
    // Reason:
    //   One memo slot exists for every (i, j) state.
    // -------------------------------------------------------------------------
    public static int TwoDimensionalMemo(int m, int n)
    {
        var memo = new int?[m, n];

        int Solve(int i, int j)
        {
            if (i == 0 || j == 0)
                return 1;

            if (memo[i, j] is int cached)
                return cached;

            int result = Solve(i - 1, j) + Solve(i, j - 1);
            memo[i, j] = result;
            return result;
        }

        return Solve(m - 1, n - 1);
    }


    // -------------------------------------------------------------------------
    // 18. RETAIN O(n) DATA AT EACH OF log n LEVELS
    //
    // Known Output Space: Θ(n log n)
    // Reason:
    //   log2(n) levels are retained, with Θ(n) integers stored per level.
    //
    // Assumption:
    //   n > 0.
    // -------------------------------------------------------------------------
    public static List<int[]> RetainLinearDataPerLogLevel(int n)
    {
        var levels = new List<int[]>();

        for (int size = n; size > 0; size /= 2)
        {
            levels.Add(new int[n]);

            if (size == 1)
                break;
        }

        return levels;
    }


    // -------------------------------------------------------------------------
    // 19. MATERIALIZE ALL SUBSETS
    //
    // Known Number of Output Collections: Θ(2^n)
    //
    // Known Total Output Element Storage: Θ(n * 2^n)
    //
    // Reason:
    //   There are 2^n subsets.
    //   Across all subsets, each of n input elements occurs in exactly half of
    //   the subsets, so total copied elements are n * 2^(n-1).
    //
    // Important:
    //   Saying merely "O(2^n) space" ignores the copied contents of each subset.
    // -------------------------------------------------------------------------
    public static List<List<int>> AllSubsets(int[] values)
    {
        var result = new List<List<int>>();

        void Generate(int index, List<int> current)
        {
            if (index == values.Length)
            {
                result.Add(new List<int>(current));
                return;
            }

            Generate(index + 1, current);

            current.Add(values[index]);
            Generate(index + 1, current);
            current.RemoveAt(current.Count - 1);
        }

        Generate(0, new List<int>());
        return result;
    }


    // -------------------------------------------------------------------------
    // 20. MATERIALIZE ALL PERMUTATIONS
    //
    // Known Number of Output Collections: Θ(n!)
    //
    // Known Total Output Element Storage: Θ(n * n!)
    //
    // Reason:
    //   There are n! permutations and each stored permutation contains n items.
    //
    // Auxiliary recursion space excluding output:
    //   Θ(n)
    // -------------------------------------------------------------------------
    public static List<int[]> AllPermutations(int[] values)
    {
        var result = new List<int[]>();
        var working = (int[])values.Clone();

        void Permute(int index)
        {
            if (index == working.Length)
            {
                result.Add((int[])working.Clone());
                return;
            }

            for (int i = index; i < working.Length; i++)
            {
                (working[index], working[i]) = (working[i], working[index]);
                Permute(index + 1);
                (working[index], working[i]) = (working[i], working[index]);
            }
        }

        Permute(0);
        return result;
    }


    // -------------------------------------------------------------------------
    // 21. MATERIALIZE ALL k-COMBINATIONS
    //
    // Known Number of Output Collections: Θ(C(n, k))
    //
    // Known Total Output Element Storage: Θ(k * C(n, k))
    //
    // Auxiliary recursion/working space excluding output:
    //   Θ(k)
    // -------------------------------------------------------------------------
    public static List<int[]> AllCombinations(int[] values, int k)
    {
        var result = new List<int[]>();
        var current = new int[k];

        void Generate(int start, int depth)
        {
            if (depth == k)
            {
                result.Add((int[])current.Clone());
                return;
            }

            for (int i = start; i <= values.Length - (k - depth); i++)
            {
                current[depth] = values[i];
                Generate(i + 1, depth + 1);
            }
        }

        Generate(0, 0);
        return result;
    }


    // -------------------------------------------------------------------------
    // 22. OUTPUT SIZE PARAMETERIZED BY RESULT LENGTH
    //
    // Known Output Space: Θ(L)
    // where:
    //   L = number of characters in the returned string
    //
    // Reason:
    //   Sometimes output size is better modeled by resulting length than by
    //   the original input count.
    // -------------------------------------------------------------------------
    public static string RepeatString(string value, int count)
    {
        return string.Concat(System.Linq.Enumerable.Repeat(value, count));
    }


    // -------------------------------------------------------------------------
    // 23. BREADTH-FIRST SEARCH FRONTIER + VISITED SET
    //
    // Known Auxiliary Space: O(V)
    //
    // More precise:
    //   Θ(number of visited vertices + maximum queued frontier)
    //
    // Worst case:
    //   Θ(V)
    //
    // Reason:
    //   Each vertex enters visited once and can be queued at most once.
    // -------------------------------------------------------------------------
    public static int BreadthFirstCount(List<int>[] graph, int start)
    {
        var visited = new bool[graph.Length];
        var queue = new Queue<int>();

        visited[start] = true;
        queue.Enqueue(start);

        int count = 0;

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            count++;

            foreach (int next in graph[node])
            {
                if (visited[next])
                    continue;

                visited[next] = true;
                queue.Enqueue(next);
            }
        }

        return count;
    }


    // -------------------------------------------------------------------------
    // 24. DEPTH-FIRST SEARCH ON A GRAPH
    //
    // Known Auxiliary Space: O(V)
    //
    // Components:
    //   visited array: Θ(V)
    //   recursion stack: O(V) worst case
    //
    // Overall:
    //   Θ(V) worst-case auxiliary space.
    // -------------------------------------------------------------------------
    public static int DepthFirstCount(List<int>[] graph, int start)
    {
        var visited = new bool[graph.Length];

        int Visit(int node)
        {
            if (visited[node])
                return 0;

            visited[node] = true;

            int count = 1;
            foreach (int next in graph[node])
                count += Visit(next);

            return count;
        }

        return Visit(start);
    }


    // -------------------------------------------------------------------------
    // Helpers intentionally do no retention so examples above remain valid.
    // -------------------------------------------------------------------------
    private static void Consume<T>(T value)
    {
        GC.KeepAlive(value);
    }
}
