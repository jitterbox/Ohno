#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ohno.Samples.Roslyn;

/// <summary>
/// A production-shaped type that is hard for Ohno on purpose.
/// Open this file in VS Code with 0.1.6 installed and read the
/// Complexity view against the comments. The comment above each
/// member records what 0.1.6 actually reports and whether that is
/// the honest bound. Members that are still wrong or conservatively
/// opaque say so explicitly.
/// </summary>
public sealed class SessionLedger
{
    private readonly List<Session> _live = new();
    private readonly Queue<Session> _window = new();
    private readonly ConcurrentDictionary<string, Session> _hot = new();
    private readonly ImmutableDictionary<string, int> _ranks;
    private readonly IQueryable<Session> _store;
    private readonly Regex _needle;
    private readonly IScorer _scorer;

    public SessionLedger(
        IQueryable<Session> store,
        Regex needle,
        IScorer scorer,
        ImmutableDictionary<string, int> ranks)
    {
        _store = store;
        _needle = needle;
        _scorer = scorer;
        _ranks = ranks;
    }

    // Truth: Θ(n). 0.1.5 reports O(n) High — the user getter is
    // walked, not treated as a free Count. Correct.
    public int Count
    {
        get
        {
            var total = 0;
            foreach (var session in _live)
                total += session.Tags.Count;
            return total;
        }
    }

    // Truth: Θ(n). 0.1.5 reports O(n) High — the custom indexer body
    // is walked, not cataloged as List.get_Item. Correct.
    public Session? this[string id]
    {
        get
        {
            foreach (var session in _live)
            {
                if (session.Id == id) return session;
            }

            return null;
        }
    }

    // Truth: Θ(n · |line|) — the per-line int.Parse scans a string
    // whose length is an input dimension. 0.1.5 reports O(n) Medium:
    // int.Parse is allowlisted as Θ(1) (type width), so the loop
    // bound shows but the per-line scan of `raw` is hidden. The
    // headline is right only if every line is fixed-width.
    public List<int> StampIds(string[] raw)
    {
        var ids = new List<int>(raw.Length);
        foreach (var line in raw)
            ids.Add(int.Parse(line));
        return ids;
    }

    // Truth: peak Θ(k) — the else arm evicts, so the queue never
    // exceeds k+1. 0.1.5 reports O(1)/O(1) Medium, which is wrong in
    // the other direction: the Enqueue is cataloged amortized O(1),
    // and because HeapBoundDetector wants `Count > k` in the true arm
    // (not `<= k` with an else-Dequeue), no heap bound is recorded at
    // all, so even the O(k) peak is invisible. This is a real hole:
    // an unbounded structure reported as constant space.
    public void Remember(Session session, int k)
    {
        _window.Enqueue(session);
        if (_window.Count <= k)
            return;
        else
            _window.Dequeue();
    }

    // Truth: backtracking, pattern-dependent. `_needle` arrives as a
    // field — its options are not visible — so 0.1.5 correctly wipes
    // to O(unknown) / O(n) at Unknown, even if the caller built it
    // NonBacktracking. Correct: the engine cannot see the option.
    public List<Session> Search(string text)
    {
        var hits = new List<Session>();
        foreach (var session in _live)
        {
            if (_needle.IsMatch(session.Id + text))
                hits.Add(session);
        }

        return hits;
    }

    // Truth: Θ(|text|), NonBacktracking. The option sits in a local,
    // so RegexFacts cannot see the enum field at the constructor and
    // 0.1.5 reports O(unknown) Unknown. Conservative but honest — the
    // inline-constructed form (option passed directly) IS detected.
    public bool LooksSafe(string text)
    {
        var options = RegexOptions.NonBacktracking;
        return new Regex("^[a-z]+$", options).IsMatch(text);
    }

    // Truth: the provider runs the tree; no honest SQL bound. 0.1.5
    // reports O(unknown) / O(n) at Unknown — the queryable is opaque
    // (delegated to EF/LINQ-to-SQL) but the materializing ToList is
    // a real O(n) buffer. Correct.
    public List<Session> RecentPaid()
    {
        return _store
            .Where(s => s.Paid)
            .OrderByDescending(s => s.Started)
            .ToList();
    }

    // Truth: Θ(n log n) — not Θ(n). `_hot.TryGetValue` is O(1)
    // expected (ConcurrentDictionary is hash-based), but `_ranks[id]`
    // is an ImmutableDictionary, an AVL tree, so each lookup is
    // O(log n). Over n ids that dominates. 0.1.5 reports O(n log n)
    // Medium. Correct; the Θ(n) intuition assumed `_ranks` hashed.
    public int HotScore(string[] ids)
    {
        var sum = 0;
        foreach (var id in ids)
        {
            if (_hot.TryGetValue(id, out var session))
                sum += session.Score;
            sum += _ranks[id];
        }

        return sum;
    }

    // Truth: Θ(n) — memoized, each i computed once. 0.1.5 reports
    // O(C(T(Walk)) + n) at Low: the 1-D memo recurrence is not a
    // first-class idiom, so the self-call stays an opaque C(T(Walk)).
    // Conservative, not wrong — it declines to invent O(n).
    public int LongestTag(string word)
    {
        var memo = new int[word.Length + 1];
        Array.Fill(memo, -1);
        return Walk(word, 0, memo);
    }

    private static int Walk(string word, int i, int[] memo)
    {
        if (i >= word.Length) return 0;
        if (memo[i] >= 0) return memo[i];
        var take = 1 + Walk(word, i + 1, memo);
        var skip = Walk(word, i + 1, memo);
        return memo[i] = Math.Max(take, skip);
    }

    // Truth: Dijkstra is O((n + m) log n) in vertices n and edges m.
    // The source declares only n; no m variable exists. 0.1.5 reports
    // O(n² log n) Medium — the worst case in n alone (a dense graph,
    // m = Θ(n²)), with a note that an independent edge count is not
    // visible. Sparse graphs are cheaper than shown; the bound is
    // honest, not invented.
    public int[] Shortest(List<List<(int To, int W)>> graph, int start)
    {
        var dist = Enumerable.Repeat(int.MaxValue, graph.Count).ToArray();
        var heap = new PriorityQueue<int, int>();
        dist[start] = 0;
        heap.Enqueue(start, 0);
        while (heap.Count > 0)
        {
            heap.TryDequeue(out var node, out var cost);
            if (cost != dist[node]) continue;
            foreach (var (to, w) in graph[node])
            {
                var next = cost + w;
                if (next >= dist[to]) continue;
                dist[to] = next;
                heap.Enqueue(to, next);
            }
        }

        return dist;
    }

    // Truth: hard-opaque — MoveNextAsync cost is not local. 0.1.5
    // reports O(unknown) / O(1) at Unknown, not a fabricated O(n).
    // Correct.
    public async Task<int> DrainAsync(IAsyncEnumerable<int> stream)
    {
        var total = 0;
        await foreach (var value in stream)
            total += value;
        return total;
    }

    // Truth: the compiled body is data. 0.1.5 reports O(unknown)
    // Unknown — a hard wipe, correctly refusing to bound Compile.
    public Func<Session, bool> Compile(string field, int min)
    {
        var session = Expression.Parameter(typeof(Session), "s");
        var member = Expression.Property(session, field);
        var body = Expression.GreaterThanOrEqual(
            member, Expression.Constant(min));
        return Expression.Lambda<Func<Session, bool>>(body, session)
            .Compile();
    }

    // Truth: the returned line is Θ(m + n + p) to materialize
    // (Concat sums). The dead `zipped` is deferred and never runs.
    // 0.1.5 reports O(m + n + p) High. Correct — select just the Zip
    // line and it reports the Zip bound separately.
    public string[] Merge(string[] left, string[] mid, string[] right)
    {
        var zipped = left.Zip(mid, right, (a, b, c) => a + b + c);
        return left.Concat(mid).Concat(right).ToArray();
    }

    // Truth: Θ(n · cost(Score)). 0.1.5 reports O(n C(Score)) Low —
    // the interface call is not assumed O(1); the loop bound is kept
    // and the unresolved Score is named. Correct soft dispatch.
    public int TotalScore()
    {
        var total = 0;
        foreach (var session in _live)
            total += _scorer.Score(session);
        return total;
    }

    // Truth: peak Θ(k). Unlike Remember (which hides the bound in an
    // else arm), this is the shape HeapBoundDetector DOES match —
    // `Count > k` in the true arm with a Dequeue. 0.1.5 reports
    // O(1)/O(k) Medium. Correct, and the asymmetry with Remember
    // (O(1) space) shows the detector only fires on this spelling.
    public void RememberBounded(Session session, int k)
    {
        _window.Enqueue(session);
        if (_window.Count > k)
            _window.Dequeue();
    }

    // Truth: Θ(n²) — repeated string += copies the whole accumulator
    // each step. The concatenation cost grows with the running length,
    // not the loop index. Expect O(n²), not O(n).
    public string ConcatAll(string[] parts)
    {
        var text = "";
        foreach (var part in parts)
            text += part;
        return text;
    }

    // Truth: Θ(n) — Range is lazy and Sum forces it once. 0.1.5
    // reports O(1) High, which is an UNDERCOUNT: the deferred Range
    // source is not sized by n, so Sum has nothing to multiply. This
    // is a latent hole — a deferred LINQ source over a sized arg.
    public int SumRange(int n)
    {
        return Enumerable.Range(0, n).Sum();
    }

    // Truth: Θ(n) for the ToList materialization. The Where is
    // deferred and never enumerated on its own; only the ToList
    // scans. Expect one O(n) pass, not two.
    public List<Session> LivePaid()
    {
        var query = _live.Where(s => s.Paid);
        return query.ToList();
    }

    // Truth: Θ(n log n) — OrderBy sorts. A sort that returns into a
    // fluent chain is still a sort; it must never collapse to O(n).
    public Session[] ByScore()
    {
        return _live.OrderBy(s => s.Score).ToArray();
    }

    // Truth: Θ(size of _window) — a Queue drain is a worklist that
    // shrinks. 0.1.5 reports O(1) High, an UNDERCOUNT: the field
    // `_window` is never seeded with a size, so the while bound has
    // nothing to follow and the drain collapses to constant. Same
    // class of hole as Remember — an unseeded collection field.
    public int Drain()
    {
        var total = 0;
        while (_window.Count > 0)
            total += _window.Dequeue().Score;
        return total;
    }

    // 1. Truth: Θ(n) — the loop bound is a property of the parameter.
    // 0.1.5 reports O(n) Medium (string.Length on each id is
    // cataloged expected). Correct.
    public int CountLong(string[] ids)
    {
        var n = 0;
        for (var i = 0; i < ids.Length; i++)
            if (ids[i].Length > 8) n++;
        return n;
    }

    // 2. Truth: Θ(n²) — classic triangular nested loop. 0.1.5
    // reports O(n²) High, not O(n). Correct.
    public long Pairs(int[] values)
    {
        var total = 0L;
        for (var i = 0; i < values.Length; i++)
            for (var j = i + 1; j < values.Length; j++)
                total += values[i] * values[j];
        return total;
    }

    // 3. Truth: Θ(log n) — halving loop. 0.1.5 reports O(log n)
    // Medium. Correct.
    public int Halve(int n)
    {
        var steps = 0;
        while (n > 1)
        {
            n /= 2;
            steps++;
        }
        return steps;
    }

    // 4. Truth: Θ(log n) — binary search. 0.1.5 reports O(log n)
    // Medium: a sorted-array mid-halving is not read as a linear
    // scan. Correct.
    public int Find(int[] sorted, int target)
    {
        var lo = 0;
        var hi = sorted.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] == target) return mid;
            if (sorted[mid] < target) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }

    // 5. Truth: Θ(n) — HashSet dedup. 0.1.5 reports O(n)/O(n)
    // Medium: each Add is O(1) expected. Correct.
    public int Unique(string[] ids)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
            seen.Add(id);
        return seen.Count;
    }

    // 6. Truth: Θ(n) — StringBuilder amortized append is linear in
    // the total output, unlike string +=. 0.1.5 reports O(n)/O(n)
    // Medium. Contrast ConcatAll (O(n²)). Correct.
    public string BuildAll(string[] parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
            sb.Append(part);
        return sb.ToString();
    }

    // 7. Truth: Θ(n log n) — List.Sort dominates. 0.1.5 reports
    // O(n log n)/O(n) High even beside the linear ToList/ToArray.
    // Correct.
    public int[] SortCopy(int[] values)
    {
        var copy = values.ToList();
        copy.Sort();
        return copy.ToArray();
    }

    // 8. Truth: Θ(n · m) — for each of n sessions, scan m tags. 0.1.5
    // reports O(n²): the nested Tags list is read as the same n
    // dimension, not a separate m. Slightly pessimistic for sparse
    // tags; honest when tags are dense.
    public int TaggedPairs()
    {
        var n = 0;
        foreach (var session in _live)
            foreach (var tag in session.Tags)
                n += tag.Length;
        return n;
    }

    // 9. Truth: Θ(n) — early return inside a loop does not lower the
    // worst case. 0.1.5 reports O(n) High, not O(1). Correct.
    public bool HasId(string id)
    {
        foreach (var session in _live)
            if (session.Id == id) return true;
        return false;
    }

    // 10. Truth: Θ(n · |Id|) — string.Contains scans each Id. 0.1.5
    // reports O(n²): the per-item scan is sized by the loop dimension,
    // so n sessions × n-length Ids. Correct when Ids grow with input.
    public int WithTag(string needle)
    {
        var n = 0;
        foreach (var session in _live)
            if (session.Id.Contains(needle)) n++;
        return n;
    }

    // 11. Truth: Θ(n²) — List.Insert(0, x) shifts every element.
    // 0.1.5 reports O(n²) High. Correct.
    public List<int> ReverseNaive(int[] values)
    {
        var list = new List<int>();
        foreach (var v in values)
            list.Insert(0, v);
        return list;
    }

    // 12. Truth: Θ(n) — Dictionary group-by. 0.1.5 reports O(n)/O(n)
    // Medium: TryGetValue/set_Item are O(1) expected. Correct.
    public Dictionary<string, int> TallyByFirst(string[] ids)
    {
        var tally = new Dictionary<string, int>();
        foreach (var id in ids)
        {
            var key = id[..1];
            tally.TryGetValue(key, out var n);
            tally[key] = n + 1;
        }
        return tally;
    }

    // 13. Truth: Θ(n + q) — n to build, q lookups. 0.1.5 reports
    // O(m + n): the two parameters map to distinct dimensions (ids→n,
    // queries→m). Correct — both independent dimensions appear.
    public int LookupAll(string[] ids, string[] queries)
    {
        var set = new HashSet<string>(ids);
        var hits = 0;
        foreach (var q in queries)
            if (set.Contains(q)) hits++;
        return hits;
    }

    // 14. Truth: Θ(n log n) — GroupBy then OrderByDescending. 0.1.5
    // reports O(n log n)/O(n) High. The sort in the chain is not
    // collapsed to a linear scan. Correct.
    public string[] TopTags()
    {
        return _live
            .SelectMany(s => s.Tags)
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToArray();
    }

    // 15. Truth: Θ(n) time, Θ(1) extra space — a 100-wide sliding
    // window. 0.1.5 reports O(n)/O(1) Medium: HeapBoundDetector
    // recognizes `Count > 100` + Dequeue. Correct.
    public long SlidingSum(int[] values)
    {
        var window = new Queue<int>();
        long sum = 0;
        long best = 0;
        foreach (var v in values)
        {
            window.Enqueue(v);
            sum += v;
            if (window.Count > 100)
                sum -= window.Dequeue();
            if (sum > best) best = sum;
        }
        return best;
    }

    // 16. Truth: Θ(n · m) — jagged array walk. 0.1.5 reports O(k n):
    // the inner row length is a fresh dimension k, not m. Same
    // product, different name. Correct two-dimension bound.
    public long JaggedSum(int[][] grid)
    {
        long total = 0;
        foreach (var row in grid)
            foreach (var cell in row)
                total += cell;
        return total;
    }

    // 17. Truth: Θ(2^n) — naive Fibonacci, two self-calls per level.
    // 0.1.5 reports O(2^n)/O(n) Medium. Never collapsed to O(n).
    // Correct.
    public int Fib(int n)
    {
        if (n < 2) return n;
        return Fib(n - 1) + Fib(n - 2);
    }

    // 18. Truth: Θ(n) — recursion depth n with O(1) work per frame.
    // 0.1.5 reports O(n)/O(n) Medium: linear recursion, distinct from
    // the Fib shape. The O(n) space is the call stack. Correct.
    public int CountDown(int n)
    {
        if (n <= 0) return 0;
        return 1 + CountDown(n - 1);
    }

    // 19. Truth: Θ(n) — LINQ Count(predicate) forces a full scan.
    // 0.1.5 reports O(n) High: a filtered count is not the O(1)
    // Count property. Correct.
    public int PaidCount()
    {
        return _live.Count(s => s.Paid);
    }

    // 20. Truth: Θ(n log n + q log n) — Sort is O(n log n), then q
    // binary searches at O(log n) each. 0.1.5 reports
    // O(m log n + n log n): both the sort and the per-probe search
    // are sized, with probes→m. Correct two-dimension bound.
    public int[] Ranked(int[] values, int[] probes)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var result = new int[probes.Length];
        for (var i = 0; i < probes.Length; i++)
            result[i] = Array.BinarySearch(sorted, probes[i]);
        return result;
    }

    // 21. Truth: Θ(n) — LinkedList.Find walks the chain. 0.1.5
    // reports O(n) High, not the O(1) of AddLast. Correct.
    public bool InChain(LinkedList<string> chain, string id)
    {
        return chain.Find(id) is not null;
    }

    // 22. Truth: Θ(log n) — SortedSet.Contains is a tree walk.
    // 0.1.5 reports O(log n) High. Correct.
    public bool RankedHas(SortedSet<string> ranks, string id)
    {
        return ranks.Contains(id);
    }

    // 23. Truth: Θ(n) to freeze. 0.1.5 reports
    // O(C(ToFrozenDictionary)) Low: the extension is cataloged on
    // FrozenDictionary, but the call is Enumerable.ToFrozenDictionary
    // and misses. Catalog-key hole.
    public bool FrozenHas(string[] ids, string id)
    {
        var frozen = ids.ToFrozenDictionary(x => x, x => 1);
        return frozen.ContainsKey(id);
    }

    // 24. Truth: Θ(n) — ConcurrentQueue drain of a parameter. 0.1.5
    // reports O(n) Medium. Contrast Drain (field, O(1)): a parameter
    // is sized, a field is not.
    public int DrainQueue(ConcurrentQueue<int> q)
    {
        var total = 0;
        while (q.TryDequeue(out var v))
            total += v;
        return total;
    }

    // 25. Truth: Θ(n log n) — ImmutableHashSet.Add is a HAMT insert.
    // 0.1.5 reports O(n C(Add)) Low: Add is not in the catalog, so
    // each insert stays opaque. Catalog hole.
    public ImmutableHashSet<string> FreezeIds(string[] ids)
    {
        var set = ImmutableHashSet<string>.Empty;
        foreach (var id in ids)
            set = set.Add(id);
        return set;
    }

    // 26. Truth: Θ(n) — a full-span walk. 0.1.5 reports O(n) High.
    // Correct.
    public int SpanSum(int[] values)
    {
        var span = values.AsSpan();
        var total = 0;
        foreach (var v in span)
            total += v;
        return total;
    }

    // 27. Truth: Θ(n²) — StringBuilder.Insert(0, x) shifts. 0.1.5
    // reports O(n²)/O(n) High. Correct.
    public string ReverseBuild(string[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
            sb.Insert(0, part);
        return sb.ToString();
    }

    // 28. Truth: Θ(n) — Array.Copy is linear. 0.1.5 reports
    // O(n)/O(n) High. Correct.
    public int[] CloneAll(int[] values)
    {
        var copy = new int[values.Length];
        Array.Copy(values, copy, values.Length);
        return copy;
    }

    // 29. Truth: Θ(n · m) — SelectMany flattens tags. 0.1.5 reports
    // O(n) High: the inner sequence is not given its own dimension.
    // Undercount when tags are long.
    public int FlatTagChars()
    {
        return _live.SelectMany(s => s.Tags).Sum(t => t.Length);
    }

    // 30. Truth: Θ(n + m) — Join hashes then probes. 0.1.5 reports
    // O(C(Join) + n) Low: Join is not cataloged at this arity.
    // Catalog hole.
    public int PaidJoin(Session[] extra)
    {
        return _live.Join(extra, a => a.Id, b => b.Id, (a, b) => 1)
            .Count();
    }

    // 31. Truth: Θ(n) — ToDictionary hashes every element. 0.1.5
    // reports O(n)/O(n) High. Correct.
    public Dictionary<string, int> IndexById()
    {
        return _live.ToDictionary(s => s.Id, s => s.Score);
    }

    // 32. Truth: Θ(n) — Aggregate walks once. 0.1.5 reports O(n)
    // High. Correct.
    public int ScoreSum()
    {
        return _live.Aggregate(0, (n, s) => n + s.Score);
    }

    // 33. Truth: Θ(n) worst — Any(pred) is a full scan. 0.1.5
    // reports O(n) High, not O(1). Correct.
    public bool AnyPaid()
    {
        return _live.Any(s => s.Paid);
    }

    // 34. Truth: Θ(n + m) — Intersect builds a set then probes.
    // 0.1.5 reports O(m + n) High. Correct.
    public int Shared(string[] left, string[] right)
    {
        return left.Intersect(right).Count();
    }

    // 35. Truth: Θ(n) time, Θ(n) space to materialize chunks.
    // 0.1.5 reports O(n)/O(1) High: Count forces the walk but the
    // chunk buffers are not charged. Space undercount.
    public int ChunkCount(int[] values)
    {
        return values.Chunk(16).Count();
    }

    // 36. Truth: Θ(n) — FirstOrDefault(pred) is a worst-case scan.
    // 0.1.5 reports O(n) High. Correct.
    public Session? FirstPaid()
    {
        return _live.FirstOrDefault(s => s.Paid);
    }

    // 37. Truth: Θ(n) — do/while walks once. 0.1.5 reports O(n)
    // High. Correct.
    public int DoSum(int[] values)
    {
        if (values.Length == 0) return 0;
        var i = 0;
        var total = 0;
        do
        {
            total += values[i];
            i++;
        }
        while (i < values.Length);
        return total;
    }

    // 38. Truth: Θ(n) — while(true) + break is a linear scan. 0.1.5
    // reports O(n) Medium. Correct.
    public int BreakSum(int[] values)
    {
        var i = 0;
        var total = 0;
        while (true)
        {
            if (i >= values.Length) break;
            total += values[i];
            i++;
        }
        return total;
    }

    // 39. Truth: Θ(log n) — doubling until n. 0.1.5 reports O(n)
    // High: `x *= 2` is not recognized as a logarithmic update
    // (only `/= 2` and `>>= 1` are). Loop-bound hole.
    public int DoubleUp(int n)
    {
        var x = 1;
        var steps = 0;
        while (x < n)
        {
            x *= 2;
            steps++;
        }
        return steps;
    }

    // 40. Truth: Θ(log n) — right-shift countdown. 0.1.5 reports
    // O(log n) Medium. Correct — contrast DoubleUp.
    public int ShiftDown(int n)
    {
        var steps = 0;
        while (n > 0)
        {
            n >>= 1;
            steps++;
        }
        return steps;
    }

    // 41. Truth: the source is an IEnumerable — no written size.
    // 0.1.5 reports O(n) High, inventing a dimension the source
    // does not declare. Honesty-rule hole.
    public int WalkUnknown(IEnumerable<int> source)
    {
        var total = 0;
        foreach (var v in source)
            total += v;
        return total;
    }

    // 42. Truth: Θ(n) — goto-as-loop is a linear walk. 0.1.5
    // reports O(1) High: goto is not a loop, so the walk is free.
    // Undercount hole.
    public int GotoSum(int[] values)
    {
        var i = 0;
        var total = 0;
    again:
        if (i >= values.Length) return total;
        total += values[i];
        i++;
        goto again;
    }

    // 43. Truth: Θ(φ^n) — mutual recursion. 0.1.5 reports
    // O(unknown) Unknown for both Ping and Pong: the cycle is not
    // a named recurrence. Honest wipe, never O(n).
    public int Ping(int n) => n < 2 ? n : Pong(n - 1) + Ping(n - 2);

    public int Pong(int n) => n < 2 ? n : Ping(n - 1) + Pong(n - 2);

    // 44. Truth: Θ(n log n) — divide-and-conquer merge. 0.1.5
    // reports O(C(T(MergeSort))) Low: the split recurrence is not
    // a first-class idiom. Conservative, not O(n).
    public int[] MergeSort(int[] values)
    {
        if (values.Length <= 1) return values;
        var mid = values.Length / 2;
        var left = MergeSort(values[..mid]);
        var right = MergeSort(values[mid..]);
        return MergeSorted(left, right);
    }

    private static int[] MergeSorted(int[] left, int[] right)
    {
        var result = new int[left.Length + right.Length];
        var i = 0;
        var j = 0;
        var k = 0;
        while (i < left.Length && j < right.Length)
            result[k++] = left[i] <= right[j] ? left[i++] : right[j++];
        while (i < left.Length) result[k++] = left[i++];
        while (j < right.Length) result[k++] = right[j++];
        return result;
    }

    // 45. Truth: Θ(n) — tail-shaped countdown. 0.1.5 reports
    // O(n)/O(n) Medium, same as CountDown. Correct.
    public int TailDown(int n, int acc)
    {
        if (n <= 0) return acc;
        return TailDown(n - 1, acc + 1);
    }

    // 46. Truth: Θ(n · m) — 2-D DP table. 0.1.5 reports
    // O(m n)/O(m n) High. Correct.
    public int GridPaths(int n, int m)
    {
        var dp = new int[n, m];
        for (var i = 0; i < n; i++)
            dp[i, 0] = 1;
        for (var j = 0; j < m; j++)
            dp[0, j] = 1;
        for (var i = 1; i < n; i++)
            for (var j = 1; j < m; j++)
                dp[i, j] = dp[i - 1, j] + dp[i, j - 1];
        return dp[n - 1, m - 1];
    }

    // 47. Truth: Θ(n + m) — BFS with visited. 0.1.5 reports
    // O(n²)/O(n) Medium: same nested-list rule as Shortest — no
    // independent m, so the dense-graph bound. Honest.
    public int Breadth(List<List<int>> graph, int start)
    {
        var seen = new bool[graph.Count];
        var q = new Queue<int>();
        seen[start] = true;
        q.Enqueue(start);
        var n = 0;
        while (q.Count > 0)
        {
            var node = q.Dequeue();
            n++;
            foreach (var next in graph[node])
            {
                if (seen[next]) continue;
                seen[next] = true;
                q.Enqueue(next);
            }
        }
        return n;
    }

    // 48. Truth: may not terminate — BFS without visited. 0.1.5
    // reports O(unknown) Unknown. Correct wipe.
    public int BreadthOpen(List<List<int>> graph, int start)
    {
        var q = new Queue<int>();
        q.Enqueue(start);
        var n = 0;
        while (q.Count > 0)
        {
            var node = q.Dequeue();
            n++;
            foreach (var next in graph[node])
                q.Enqueue(next);
        }
        return n;
    }

    // 49. Truth: Θ(n³) — Floyd–Warshall. 0.1.5 reports O(n³) High.
    // Correct.
    public void Floyd(int[,] dist)
    {
        var n = dist.GetLength(0);
        for (var k = 0; k < n; k++)
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    if (dist[i, k] + dist[k, j] < dist[i, j])
                        dist[i, j] = dist[i, k] + dist[k, j];
    }

    // 50. Truth: Θ(n + m · α(n)) — Union-Find. 0.1.5 reports
    // O(m + n) High: the inverse-Ackermann factor is not modeled,
    // so the bound is the loop sizes. Close and honest.
    public int Components(int n, (int A, int B)[] edges)
    {
        var p = new int[n];
        for (var i = 0; i < n; i++) p[i] = i;
        foreach (var (a, b) in edges)
            p[FindRoot(p, a)] = FindRoot(p, b);
        var c = 0;
        for (var i = 0; i < n; i++)
            if (p[i] == i) c++;
        return c;
    }

    private static int FindRoot(int[] p, int x)
    {
        while (p[x] != x)
        {
            p[x] = p[p[x]];
            x = p[x];
        }
        return x;
    }

    // 51. Truth: hard-opaque — dynamic dispatch. 0.1.5 reports
    // O(unknown) Unknown. Correct wipe.
    public int DynamicScore(dynamic scorer, Session session)
    {
        return scorer.Score(session);
    }

    // 52. Truth: hard-opaque — reflection Invoke. 0.1.5 reports
    // O(unknown) Unknown. Correct wipe.
    public int ReflectScore(object scorer, Session session)
    {
        var method = scorer.GetType().GetMethod("Score");
        return (int)method!.Invoke(scorer, new object[] { session })!;
    }

    // 53. Truth: Θ(n · C(Equals)) — unconstrained T.Equals is not
    // O(1). 0.1.5 reports O(n) High: Equals is treated as constant
    // (Object.Equals allowlist). Honesty hole for a generic T.
    public int CountEqual<T>(T[] values, T needle)
    {
        var n = 0;
        foreach (var v in values)
            if (v!.Equals(needle)) n++;
        return n;
    }

    // 54. Truth: Θ(n log n · C(Compare)) — Sort with an IComparer
    // is still a sort. 0.1.5 reports O(n log n) High. The comparer
    // cost is not named, but the sort is not collapsed. Mostly
    // correct.
    public void SortBy(List<Session> sessions, IComparer<Session> cmp)
    {
        sessions.Sort(cmp);
    }

    // 55. Truth: Θ(n) — a local function in a loop. 0.1.5 reports
    // O(n) High: ScoreOf is inlined, not left as C(ScoreOf).
    // Correct.
    public int LocalScore()
    {
        int ScoreOf(Session s) => s.Score + s.Tags.Count;
        var total = 0;
        foreach (var session in _live)
            total += ScoreOf(session);
        return total;
    }

    public sealed record Session(
        string Id,
        bool Paid,
        DateTime Started,
        int Score,
        List<string> Tags);

    public interface IScorer
    {
        int Score(Session session);
    }
}
