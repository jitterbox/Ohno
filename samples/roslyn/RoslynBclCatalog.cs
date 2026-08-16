using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Ohno.Samples.Roslyn;

/// <summary>
/// Everyday BCL surface, on purpose.
/// </summary>
/// <remarks>
/// The rest of the corpus was written against the members the catalog
/// already knew, so it could not see a whole class of failure: an
/// uncataloged member used to be costed as O(1), which silently erased
/// sorts and scans, while an uncataloged <c>string</c> member went the
/// other way and dragged the method to Low confidence.
/// <para>
/// Every method here uses the APIs real C# uses — overloads with
/// comparers and selectors, string manipulation, spans, LINQ set
/// operators. A regression in the catalog shows up here as a bound
/// that changes, not as a silent constant.
/// </para>
/// </remarks>
public static class RoslynBclCatalog
{
    // Known Time: Θ(n log n) — the comparer overload sorts just as the
    // parameterless one does. This is the case that used to report O(1).
    public static string[] SortWithComparer(string[] names)
    {
        return names
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    // Known Time: Θ(n log n)
    // .NET 9's parameterless Order() must sort like OrderBy does.
    public static int[] OrderShorthand(int[] values)
    {
        return values.Order().ToArray();
    }

    // Known Time: Θ(n) — the selector overload scans every element.
    public static int SumWithSelector(Item[] items)
    {
        return items.Sum(item => item.Weight);
    }

    // Known Time: Θ(n)
    public static Item? HeaviestItem(Item[] items)
    {
        return items.MaxBy(item => item.Weight);
    }

    // Known Time: Θ(m + n) — two independent sources, one hash set.
    // Known Auxiliary Space: Θ(m + n)
    public static int[] SharedValues(int[] left, int[] right)
    {
        return left.Intersect(right).ToArray();
    }

    // Known Time: Θ(m + n)
    // Known Auxiliary Space: Θ(m + n)
    public static int[] Combined(int[] left, int[] right)
    {
        return left.Concat(right).ToArray();
    }

    // Known Time: Θ(n)
    // Known Auxiliary Space: Θ(n)
    public static HashSet<int> UniqueValues(int[] values)
    {
        return values.ToHashSet();
    }

    // Known Time: Θ(n) — Split allocates one string per field.
    // Known Auxiliary Space: Θ(n)
    public static string[] SplitFields(string line)
    {
        return line.Split(',');
    }

    // Known Time: Θ(n) — Substring copies, it does not alias.
    // Known Auxiliary Space: Θ(n)
    public static string Tail(string text, int start)
    {
        return text.Substring(start);
    }

    // Known Time: Θ(n) — a scan, with no allocation.
    public static bool Mentions(string text, string term)
    {
        return text.IndexOf(term, StringComparison.Ordinal) >= 0;
    }

    // Known Time: Θ(n)
    // Known Auxiliary Space: Θ(n)
    public static string Normalize(string text)
    {
        return text.Trim().ToUpperInvariant();
    }

    // Known Time: Θ(n) — Join walks the source and copies each piece.
    // Known Auxiliary Space: Θ(n)
    public static string JoinNames(string[] names)
    {
        return string.Join(", ", names);
    }

    // Known Time: Θ(n) — amortized appends, one materialization.
    // Known Auxiliary Space: Θ(n)
    public static string BuildReport(string[] lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    // Known Time: Θ(n) — a vectorized scan over the span.
    public static int FirstSeparator(ReadOnlySpan<char> text)
    {
        return text.IndexOf(';');
    }

    // Known Time: Θ(n log n) — sorting a span is still a sort.
    public static void SortInPlace(Span<int> values)
    {
        values.Sort();
    }

    // Known Time: Θ(n) to build, then Θ(1) per lookup.
    // Known Auxiliary Space: Θ(n)
    public static FrozenSet<string> BuildLookup(string[] words)
    {
        return words.ToFrozenSet(StringComparer.Ordinal);
    }

    // Known Time: Θ(n) — SearchValues is built once, then scanned with.
    public static int CountVowels(string text)
    {
        var vowels = SearchValues.Create("aeiou");
        var span = text.AsSpan();
        var found = 0;
        var index = span.IndexOfAny(vowels);
        while (index >= 0)
        {
            found++;
            span = span.Slice(index + 1);
            index = span.IndexOfAny(vowels);
        }

        return found;
    }

    // Known Time: Θ(n) — a capacity reservation plus a linear copy.
    // Known Auxiliary Space: Θ(n)
    public static List<int> Reserved(int[] values)
    {
        var copy = new List<int>(values.Length);
        foreach (var value in values)
        {
            copy.Add(value);
        }

        return copy;
    }

    // Known Time: Θ(n + m) — two sets, walked once each.
    // Known Auxiliary Space: Θ(n)
    public static HashSet<int> Merge(HashSet<int> left, int[] right)
    {
        var merged = new HashSet<int>(left);
        merged.UnionWith(right);
        return merged;
    }

    // Known Time: Θ(n) — TryAdd is an expected-constant hash write.
    // Known Auxiliary Space: Θ(n)
    public static Dictionary<int, int> FirstIndexOfEach(int[] values)
    {
        var first = new Dictionary<int, int>();
        for (var i = 0; i < values.Length; i++)
        {
            first.TryAdd(values[i], i);
        }

        return first;
    }

    // Known Time: Θ(n) — Array.Copy moves every element.
    // Known Auxiliary Space: Θ(n)
    public static int[] Duplicate(int[] values)
    {
        var copy = new int[values.Length];
        Array.Copy(values, copy, values.Length);
        return copy;
    }

    // Known Time: Θ(n) — Reverse is a linear walk, not a view.
    public static void FlipInPlace(int[] values)
    {
        Array.Reverse(values);
    }

    // Known Time: Θ(k n) — n strings, each of length k, each sorted is
    // out of scope here; this is the plain per-element scan.
    // Known Auxiliary Space: Θ(k n)
    public static string[] NormalizeAll(string[] lines)
    {
        return lines.Select(line => line.Trim()).ToArray();
    }

    // Known Time: Θ(1) — deferred construction only. The cost belongs
    // to whoever enumerates it.
    public static IEnumerable<int> LazyEvens(int[] values)
    {
        return values.Where(value => value % 2 == 0);
    }

    // Known Time: Θ(1) — Math and char classification are fixed width.
    public static int Clamp(int value, int low, int high)
    {
        return Math.Clamp(value, low, high);
    }

    // Known Time: Θ(n + m + p) — three independent strings.
    public static string GlueThree(string left, string mid, string right)
    {
        return string.Concat(left, mid, right);
    }

    // Known Time: Θ(1) — List indexer is a stored slot.
    public static int AtList(List<int> items, int index)
    {
        return items[index];
    }

    // Known Time: Θ(log n) — SortedList indexer is a tree walk.
    public static int AtSorted(SortedList<int, int> items, int key)
    {
        return items[key];
    }

    // Known Time: Θ(n) — ImmutableList indexer walks the spine.
    public static int AtImmutable(ImmutableList<int> items, int index)
    {
        return items[index];
    }

    public sealed class Item
    {
        public int Weight { get; init; }
    }
}
