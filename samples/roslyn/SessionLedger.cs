#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ohno.Samples.Roslyn;

/// <summary>
/// A production-shaped type that is hard for Ohno on purpose.
/// Open this file in VS Code with 0.1.4 installed and read the
/// Complexity view against the comments. Several members are
/// honest Unknowns; a few are still the wrong tight bound.
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

    // Truth: Θ(n). 0.1.4 walks a user Count. Confirm the panel
    // says O(n) and not O(1).
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

    // Truth: Θ(n). Custom indexer, not List.get_Item.
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

    // Truth: Θ(n) parse of unbounded strings. Ohno allowlists
    // scalar Parse as Θ(1) (type width), so this often lands as
    // O(n) from the loop and hides the per-line scan of `raw`.
    public List<int> StampIds(string[] raw)
    {
        var ids = new List<int>(raw.Length);
        foreach (var line in raw)
            ids.Add(int.Parse(line));
        return ids;
    }

    // Truth: peak Θ(k). Eviction is in the else of `Count <= k`,
    // which HeapBoundDetector does not match (it wants Count > k
    // in the true arm). Expect O(n) space, not O(k).
    public void Remember(Session session, int k)
    {
        _window.Enqueue(session);
        if (_window.Count <= k)
            return;
        else
            _window.Dequeue();
    }

    // Truth: backtracking, pattern-dependent. `_needle` arrives
    // as a field — options are not visible — so this should wipe
    // to O(unknown), even if the caller built it NonBacktracking.
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

    // Truth: Θ(n), NonBacktracking. The option sits in a local,
    // so RegexFacts cannot see the enum field at the constructor
    // and this stays opaque.
    public bool LooksSafe(string text)
    {
        var options = RegexOptions.NonBacktracking;
        return new Regex("^[a-z]+$", options).IsMatch(text);
    }

    // Truth: the provider runs the tree; no honest SQL bound.
    // Soft opacity: the later ToList is a real scan. Expect a
    // queryable approach plus a materialize, not a invented O(n).
    public List<Session> RecentPaid()
    {
        return _store
            .Where(s => s.Paid)
            .OrderByDescending(s => s.Started)
            .ToList();
    }

    // Truth: Θ(n) expected for ConcurrentDictionary indexer.
    // Not in the catalog — expect C(get_Item) at Low, or a
    // loop of C(get_Item) if the walk treats each read as work.
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

    // Truth: Θ(n) with a 1-D memo table. RecurrenceAnalyzer
    // does not treat 1-D memo as a first-class idiom, so this
    // is often C(Walk) / data-dependent recursion / Unknown
    // instead of O(n).
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

    // Truth: Dijkstra is O((n + m) log n), not "visited graph
    // walk = O(k n)". The PQ + decrease-key shape is not a
    // named recurrence. Expect a weaker loop/worklist bound
    // or Unknown.
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

    // Truth: hard-opaque. await foreach wipes even beside a
    // resolved loop. Expect O(unknown), not O(n).
    public async Task<int> DrainAsync(IAsyncEnumerable<int> stream)
    {
        var total = 0;
        await foreach (var value in stream)
            total += value;
        return total;
    }

    // Truth: the compiled body is data. Hard wipe.
    public Func<Session, bool> Compile(string field, int min)
    {
        var session = Expression.Parameter(typeof(Session), "s");
        var member = Expression.Property(session, field);
        var body = Expression.GreaterThanOrEqual(
            member, Expression.Constant(min));
        return Expression.Lambda<Func<Session, bool>>(body, session)
            .Compile();
    }

    // Truth: Θ(n + m + p) to materialize. Zip of three is
    // closer to min(|a|,|b|,|c|); Concat of three is a sum.
    // Select the Zip line vs the Concat line and compare.
    public string[] Merge(string[] left, string[] mid, string[] right)
    {
        var zipped = left.Zip(mid, right, (a, b, c) => a + b + c);
        return left.Concat(mid).Concat(right).ToArray();
    }

    // Truth: Θ(n · C(Score)). Interface dispatch is soft when
    // a loop exists — the bound is kept and the hazard is
    // named. Confirm Score is not assumed O(1).
    public int TotalScore()
    {
        var total = 0;
        foreach (var session in _live)
            total += _scorer.Score(session);
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
