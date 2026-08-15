using System;
using System.Collections.Generic;
using System.Linq;

namespace Ohno.Samples.Roslyn;

/// <summary>
/// Algorithms that sit on the boundaries a complexity reader most
/// often gets wrong.
/// </summary>
/// <remarks>
/// BigO(Bench) and CodeComplex both report the same failure mode for
/// learned predictors: confusion between <em>hierarchically adjacent</em>
/// classes — O(n) mistaken for O(n log n), O(n log n) for O(n²). The
/// pairs here are deliberately close in shape and far apart in bound,
/// so a regression that blurs those classes shows up as a changed
/// assertion rather than as a plausible-looking number.
/// <para>
/// Every closed form below is hand-checked. Where Ohno cannot justify
/// the true bound from the source, the test records what it actually
/// reports — including <c>O(unknown)</c> — rather than dropping the
/// case, because the gap is the useful information.
/// </para>
/// </remarks>
public static class RoslynBoundaryBench
{
    // ---- O(n) against O(n log n) -------------------------------------

    // Θ(n): one pass, a hash set, no ordering.
    public static bool HasDuplicateByHash(int[] values)
    {
        var seen = new HashSet<int>();
        foreach (var value in values)
        {
            if (!seen.Add(value)) return true;
        }

        return false;
    }

    // Θ(n log n): the same question answered by sorting first.
    public static bool HasDuplicateBySort(int[] values)
    {
        var copy = (int[])values.Clone();
        Array.Sort(copy);
        for (var i = 1; i < copy.Length; i++)
        {
            if (copy[i] == copy[i - 1]) return true;
        }

        return false;
    }

    // Θ(n): running maximum, no sort despite the name.
    public static int LargestValue(int[] values)
    {
        var best = int.MinValue;
        foreach (var value in values)
        {
            if (value > best) best = value;
        }

        return best;
    }

    // Θ(n log n): k-th largest via a full sort.
    public static int KthLargestBySort(int[] values, int k)
    {
        var copy = (int[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length - k];
    }

    // ---- O(n log n) against O(n²) ------------------------------------

    // Θ(n²): insertion sort — two nested passes over the same array.
    public static void InsertionSort(int[] values)
    {
        for (var i = 1; i < values.Length; i++)
        {
            var key = values[i];
            var j = i - 1;
            while (j >= 0 && values[j] > key)
            {
                values[j + 1] = values[j];
                j--;
            }

            values[j + 1] = key;
        }
    }

    // Θ(n log n): the library sort, one call.
    public static void LibrarySort(int[] values)
    {
        Array.Sort(values);
    }

    // Θ(n²): pairwise comparison, the classic quadratic shape.
    public static int CountInversionsNaive(int[] values)
    {
        var count = 0;
        for (var i = 0; i < values.Length; i++)
        {
            for (var j = i + 1; j < values.Length; j++)
            {
                if (values[i] > values[j]) count++;
            }
        }

        return count;
    }

    // ---- O(log n) against O(n) ---------------------------------------

    // Θ(log n): halving search over a sorted array.
    public static int BinarySearchIndex(int[] sorted, int target)
    {
        var low = 0;
        var high = sorted.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] == target) return mid;
            if (sorted[mid] < target) low = mid + 1;
            else high = mid - 1;
        }

        return -1;
    }

    // Θ(n): the same answer by scanning.
    public static int LinearSearchIndex(int[] values, int target)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == target) return i;
        }

        return -1;
    }

    // ---- O(n) against O(n·m) -----------------------------------------

    // Θ(n + m): two independent scans, not a product.
    public static int SumOfBoth(int[] left, int[] right)
    {
        var total = 0;
        foreach (var value in left) total += value;
        foreach (var value in right) total += value;
        return total;
    }

    // Θ(n·m): nested, so the sizes multiply.
    public static int PairSum(int[] left, int[] right)
    {
        var total = 0;
        foreach (var a in left)
        {
            foreach (var b in right) total += a * b;
        }

        return total;
    }

    // ---- Shapes that look heavier than they are ----------------------

    // Θ(n): the inner loop is bounded by a constant, not by n.
    public static int BoundedInnerLoop(int[] values)
    {
        var total = 0;
        for (var i = 0; i < values.Length; i++)
        {
            for (var j = 0; j < 8; j++) total += values[i] + j;
        }

        return total;
    }

    // Θ(n): halving each step, but doing linear work per step is what
    // would make this n log n — it does not, so it stays linear.
    public static int HalvingWalk(int size)
    {
        var steps = 0;
        while (size > 1)
        {
            size /= 2;
            steps++;
        }

        return steps;
    }

    // Θ(n²): a triangular loop is still quadratic.
    public static int TriangularSum(int[] values)
    {
        var total = 0;
        for (var i = 0; i < values.Length; i++)
        {
            for (var j = 0; j <= i; j++) total += values[j];
        }

        return total;
    }

    // ---- Shapes that look lighter than they are ----------------------

    // Θ(n²): one visible loop, but Contains scans on every iteration.
    public static List<int> UniqueByListContains(int[] values)
    {
        var unique = new List<int>();
        foreach (var value in values)
        {
            if (!unique.Contains(value)) unique.Add(value);
        }

        return unique;
    }

    // Θ(n log n): one visible statement, and it sorts.
    public static int[] SortedCopy(int[] values)
    {
        return values.OrderBy(value => value).ToArray();
    }
}
