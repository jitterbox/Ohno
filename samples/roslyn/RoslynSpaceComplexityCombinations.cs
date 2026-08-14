using System;
using System.Collections.Generic;

namespace ComplexityFixtures;

/// <summary>
/// Combinations of the space patterns: peak vs retained, independent
/// dimensions, and recursion plus allocation must compose.
/// </summary>
public static class RoslynSpaceComplexityCombinations
{
    public static void ComboMatrixAndLinear(int n)
    {
        var row = new int[n];
        var matrix = new int[n, n];
        Consume(row);
        Consume(matrix);
    }

    public static void ComboPeakThenRetain(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var tmp = new int[n];
            Consume(tmp);
        }

        var keep = new List<int[]>();
        for (int i = 0; i < n; i++)
            keep.Add(new int[n]);

        Consume(keep);
    }

    public static int ComboWindowAndUnique(int[] values, int k)
    {
        var window = new Queue<int>(k);
        var seen = new HashSet<int>();

        foreach (int value in values)
        {
            window.Enqueue(value);
            if (window.Count > k)
                window.Dequeue();
            seen.Add(value);
        }

        return seen.Count;
    }

    public static long ComboBufferAndLinearRecursion(int n)
    {
        var buffer = new int[n];
        Consume(buffer);
        return Linear(n);
    }

    private static long Linear(int n)
    {
        if (n <= 0)
            return 0;
        return n + Linear(n - 1);
    }

    private static void Consume<T>(T value) => GC.KeepAlive(value);
}
