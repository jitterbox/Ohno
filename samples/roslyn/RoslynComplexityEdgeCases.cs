#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynComplexityFixture
{
    /// <summary>
    /// A deliberately adversarial corpus for a Roslyn-based algorithmic-complexity analyzer.
    ///
    /// The methods below are not intended as recommended production code. Each method isolates a
    /// case where a source-level Big-O estimate can be misleading, context-dependent, or impossible
    /// to make conclusive without additional assumptions, summaries, runtime type information,
    /// whole-program analysis, or an explicit definition of what "input size" and "time" mean.
    ///
    /// Suggested analyzer result vocabulary used in comments:
    /// - INCONCLUSIVE: Source alone is insufficient for a defensible bound.
    /// - CONTEXT_DEPENDENT: A useful bound is possible only after stating assumptions.
    /// - RANGE: Best/worst/consumed-path complexities differ materially.
    /// - DERIVABLE_WITH_SUMMARIES: Possible if the analyzer has trusted callee/library summaries.
    /// - NON_TERMINATION_RISK: The method may not terminate for all valid values of its static types.
    /// </summary>
    public static class ComplexityEdgeCases
    {
        private static long _sink;

        // ---------------------------------------------------------------------
        // 01. dynamic: the invocation target is selected by the runtime binder.
        // Suggested result: INCONCLUSIVE.
        // Roslyn can identify a dynamic invocation, but there is no statically fixed target body.
        // ---------------------------------------------------------------------
        public static object? DynamicDispatch(dynamic target, int n)
        {
            return target.Run(n);
        }

        // ---------------------------------------------------------------------
        // 02. Reflection: both the target method and implementation can be selected at runtime.
        // Suggested result: INCONCLUSIVE.
        // ---------------------------------------------------------------------
        public static object? ReflectionDispatch(object target, string methodName, int n)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public);

            return method?.Invoke(target, new object?[] { n });
        }

        // ---------------------------------------------------------------------
        // 03. Interface dispatch: identical call site, radically different implementations.
        // Suggested result: INCONCLUSIVE at the call site unless runtime types/call graph are bounded.
        // ---------------------------------------------------------------------
        public interface IAlgorithm
        {
            int Run(IReadOnlyList<int> values);
        }

        public sealed class LinearAlgorithm : IAlgorithm
        {
            public int Run(IReadOnlyList<int> values)
            {
                int sum = 0;
                for (int i = 0; i < values.Count; i++)
                {
                    sum += values[i];
                }

                return sum;
            }
        }

        public sealed class QuadraticAlgorithm : IAlgorithm
        {
            public int Run(IReadOnlyList<int> values)
            {
                int count = 0;
                for (int i = 0; i < values.Count; i++)
                {
                    for (int j = 0; j < values.Count; j++)
                    {
                        if (values[i] == values[j])
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        public static int InterfaceDispatch(IAlgorithm algorithm, IReadOnlyList<int> values)
        {
            return algorithm.Run(values);
        }

        // ---------------------------------------------------------------------
        // 04. Delegate parameter inside a loop.
        // Suggested result: CONTEXT_DEPENDENT: O(n * C(transform)).
        // The delegate body may be external, recursive, blocking, or itself input-dependent.
        // ---------------------------------------------------------------------
        public static int DelegateInsideLoop(IReadOnlyList<int> values, Func<int, int> transform)
        {
            int sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += transform(values[i]);
            }

            return sum;
        }

        // ---------------------------------------------------------------------
        // 05. Multicast delegate.
        // Suggested result: CONTEXT_DEPENDENT: cost also depends on invocation-list length.
        // The syntax contains one invocation, but runtime may invoke many handlers.
        // ---------------------------------------------------------------------
        public static void MulticastDelegate(Action<int> callback, int value)
        {
            callback(value);
        }

        // ---------------------------------------------------------------------
        // 06. Property access that looks field-like but executes linear work.
        // Suggested result: DERIVABLE_WITH_SUMMARIES/interprocedural analysis.
        // ---------------------------------------------------------------------
        public sealed class ExpensivePropertyHost
        {
            private readonly int[] _values;

            public ExpensivePropertyHost(int[] values)
            {
                _values = values;
            }

            public int Value
            {
                get
                {
                    int sum = 0;
                    for (int i = 0; i < _values.Length; i++)
                    {
                        sum += _values[i];
                    }

                    return sum;
                }
            }
        }

        public static int PropertyAccessLooksConstant(ExpensivePropertyHost host)
        {
            return host.Value;
        }

        // ---------------------------------------------------------------------
        // 07. Indexer syntax that looks like array O(1), but its accessor scans.
        // Suggested result: DERIVABLE_WITH_SUMMARIES/interprocedural analysis.
        // ---------------------------------------------------------------------
        public sealed class LinearIndexer
        {
            private readonly int[] _values;

            public LinearIndexer(int[] values)
            {
                _values = values;
            }

            public int this[int index]
            {
                get
                {
                    for (int i = 0; i < _values.Length; i++)
                    {
                        if (i == index)
                        {
                            return _values[i];
                        }
                    }

                    throw new IndexOutOfRangeException();
                }
            }
        }

        public static int IndexerLooksConstant(LinearIndexer values, int index)
        {
            return values[index];
        }

        // ---------------------------------------------------------------------
        // 08. User-defined operator: '+' is arbitrary executable code.
        // Suggested result: DERIVABLE_WITH_SUMMARIES if the operator body is available;
        // otherwise CONTEXT_DEPENDENT/INCONCLUSIVE.
        // ---------------------------------------------------------------------
        public readonly struct CostlyNumber
        {
            public CostlyNumber(int work)
            {
                Work = work;
            }

            public int Work { get; }

            public static CostlyNumber operator +(CostlyNumber left, CostlyNumber right)
            {
                int leftWork = Math.Max(0, left.Work);
                int rightWork = Math.Max(0, right.Work);

                for (int i = 0; i < leftWork; i++)
                {
                    _sink ^= i;
                }

                for (int i = 0; i < rightWork; i++)
                {
                    _sink ^= i;
                }

                return new CostlyNumber(Math.Max(leftWork, rightWork));
            }
        }

        public static CostlyNumber OperatorLooksConstant(CostlyNumber a, CostlyNumber b)
        {
            return a + b;
        }

        // ---------------------------------------------------------------------
        // 09. Generic static-abstract operator dispatch.
        // Suggested result: CONTEXT_DEPENDENT: O(n * C(T.+)).
        // For a fixed-width numeric T, addition may be treated as O(1); for BigInteger or a custom
        // implementation, operator cost may grow with operand size.
        // ---------------------------------------------------------------------
        public static T GenericOperatorLoop<T>(IReadOnlyList<T> values, T seed)
            where T : IAdditionOperators<T, T, T>
        {
            T total = seed;
            for (int i = 0; i < values.Count; i++)
            {
                total += values[i];
            }

            return total;
        }

        // ---------------------------------------------------------------------
        // 10. Deferred LINQ: complexity depends on whether you measure query construction or use.
        // Suggested result: RANGE/CONTEXT_DEPENDENT.
        // Creating the pipeline is not the same operation as enumerating it.
        // ---------------------------------------------------------------------
        public static IEnumerable<int> DeferredLinq(IEnumerable<int> source)
        {
            return source.Where(x => x > 0).Select(x => x * 2);
        }

        // ---------------------------------------------------------------------
        // 11. Multiple enumeration of IEnumerable<T>.
        // Suggested result: CONTEXT_DEPENDENT.
        // Count() may use a cheap Count property for some sources, enumerate others, observe changed
        // state on the second pass, or never return for an infinite sequence.
        // ---------------------------------------------------------------------
        public static int EnumerateTwice(IEnumerable<int> source)
        {
            int first = source.Count();
            int second = source.Count();
            return first + second;
        }

        // ---------------------------------------------------------------------
        // 12. Iterator/yield: call cost, partial-consumption cost, and full-consumption cost differ.
        // Suggested result: RANGE, preferably parameterized by k elements consumed.
        // ---------------------------------------------------------------------
        public static IEnumerable<int> YieldFilter(IEnumerable<int> source)
        {
            foreach (int value in source)
            {
                if ((value & 1) == 0)
                {
                    yield return value;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 13. foreach over a custom enumerator whose MoveNext is not O(1).
        // Suggested result: DERIVABLE_WITH_SUMMARIES; naïve "foreach == O(n)" is wrong.
        // This implementation rescans an increasing prefix on every MoveNext => quadratic work.
        // ---------------------------------------------------------------------
        public sealed class SlowEnumerable : IEnumerable<int>
        {
            private readonly int[] _data;
            private static int _enumerationSink;

            public SlowEnumerable(int[] data)
            {
                _data = data;
            }

            public IEnumerator<int> GetEnumerator()
            {
                return new SlowEnumerator(_data);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private sealed class SlowEnumerator : IEnumerator<int>
            {
                private readonly int[] _data;
                private int _index = -1;

                public SlowEnumerator(int[] data)
                {
                    _data = data;
                }

                public int Current => _data[_index];

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    int next = _index + 1;
                    if (next >= _data.Length)
                    {
                        return false;
                    }

                    int local = 0;
                    for (int i = 0; i <= next; i++)
                    {
                        local ^= _data[i];
                    }

                    _enumerationSink ^= local;
                    _index = next;
                    return true;
                }

                public void Reset()
                {
                    _index = -1;
                }

                public void Dispose()
                {
                }
            }
        }

        public static int ForeachOverSlowEnumerable(SlowEnumerable source)
        {
            int sum = 0;
            foreach (int value in source)
            {
                sum += value;
            }

            return sum;
        }

        // ---------------------------------------------------------------------
        // 14. IQueryable<T>: execution semantics belong to the runtime query provider.
        // Suggested result: INCONCLUSIVE for actual data-source execution cost.
        // A provider may translate this expression tree to SQL, another remote language, or local work.
        // ---------------------------------------------------------------------
        public static int QueryProviderDependent(IQueryable<int> source)
        {
            return source.Where(x => x > 0).Count();
        }

        // ---------------------------------------------------------------------
        // 15. Expression tree supplied as data, compiled into executable code at runtime.
        // Suggested result: INCONCLUSIVE unless the expression value is known and analyzed.
        // ---------------------------------------------------------------------
        public static int RuntimeExpression(Expression<Func<int, int>> expression, int value)
        {
            Func<int, int> compiled = expression.Compile();
            return compiled(value);
        }

        // ---------------------------------------------------------------------
        // 16. await around opaque work.
        // Suggested result: CONTEXT_DEPENDENT: local continuation work is tiny; awaited operation is not.
        // ---------------------------------------------------------------------
        public static async Task<int> AwaitOpaqueWork(Func<int, Task<int>> operation, int value)
        {
            return await operation(value).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------
        // 17. Async stream: item count, MoveNextAsync work, I/O latency, and cancellation are external.
        // Suggested result: CONTEXT_DEPENDENT/INCONCLUSIVE for wall-clock time.
        // ---------------------------------------------------------------------
        public static async Task<int> ConsumeAsyncStream(
            IAsyncEnumerable<int> source,
            CancellationToken cancellationToken = default)
        {
            int sum = 0;
            await foreach (int value in source.WithCancellation(cancellationToken))
            {
                sum += value;
            }

            return sum;
        }

        // ---------------------------------------------------------------------
        // 18. Regex: one library call can range from near-linear behavior to catastrophic backtracking.
        // Suggested result: DERIVABLE_WITH_SUMMARIES plus pattern analysis, otherwise CONTEXT_DEPENDENT.
        // Do not run this fixture with a huge near-miss string such as many 'a' characters plus '!'.
        // ---------------------------------------------------------------------
        private static readonly Regex BacktrackingRegex =
            new Regex(@"^(a+)+$", RegexOptions.CultureInvariant);

        public static bool RegexBacktracking(string input)
        {
            return BacktrackingRegex.IsMatch(input);
        }

        // ---------------------------------------------------------------------
        // 19. Sort complexity must compose the sorting algorithm with comparator cost.
        // Suggested result: CONTEXT_DEPENDENT in at least two dimensions: item count n and string length L.
        // ---------------------------------------------------------------------
        public static void SortWithExpensiveComparer(List<string> values)
        {
            values.Sort(SlowStringCompare);
        }

        private static int SlowStringCompare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int length = Math.Min(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                int delta = left[i].CompareTo(right[i]);
                if (delta != 0)
                {
                    return delta;
                }
            }

            return left.Length.CompareTo(right.Length);
        }

        // ---------------------------------------------------------------------
        // 20. Hash collection with an adversarial comparer.
        // Suggested result: CONTEXT_DEPENDENT; "Dictionary insertion == O(1)" is not a safe absolute rule.
        // A constant hash forces all distinct keys into the same collision set.
        // ---------------------------------------------------------------------
        public sealed class BadHashKey
        {
            public BadHashKey(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        public sealed class ConstantHashComparer : IEqualityComparer<BadHashKey>
        {
            public bool Equals(BadHashKey? x, BadHashKey? y)
            {
                return x?.Value == y?.Value;
            }

            public int GetHashCode(BadHashKey obj)
            {
                return 0;
            }
        }

        public static Dictionary<BadHashKey, int> BuildCollisionDictionary(int n)
        {
            var result = new Dictionary<BadHashKey, int>(new ConstantHashComparer());

            for (int i = 0; i < n; i++)
            {
                result[new BadHashKey(i)] = i;
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // 21. BigInteger exposes the "what is n?" problem.
        // Suggested result: CONTEXT_DEPENDENT on the size metric.
        // This is O(n) iterations if n means numeric value, but n itself requires only O(log n) bits to encode;
        // moreover, BigInteger arithmetic is not a fixed-cost primitive as operand widths grow.
        // ---------------------------------------------------------------------
        public static BigInteger CountDownBigInteger(BigInteger n)
        {
            BigInteger iterations = BigInteger.Zero;
            while (n > BigInteger.Zero)
            {
                n--;
                iterations++;
            }

            return iterations;
        }

        // ---------------------------------------------------------------------
        // 22. Collatz iteration: even termination for all positive integer inputs is not known generally.
        // Suggested result: NON_TERMINATION_RISK / INCONCLUSIVE rather than inventing a Big-O bound.
        // BigInteger avoids fixed-width integer overflow changing the mathematical sequence.
        // ---------------------------------------------------------------------
        public static BigInteger CollatzSteps(BigInteger n)
        {
            if (n <= BigInteger.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(n), "n must be positive.");
            }

            BigInteger steps = BigInteger.Zero;
            while (n != BigInteger.One)
            {
                n = n.IsEven ? n / 2 : (3 * n) + 1;
                steps++;
            }

            return steps;
        }

        // ---------------------------------------------------------------------
        // 23. Data-dependent branching recursion.
        // Suggested result: RANGE: best case linear, worst case exponential in remaining elements.
        // Reporting only one unlabeled complexity hides the most important fact.
        // ---------------------------------------------------------------------
        public static int DataDependentBranchingRecursion(int[] values, int index = 0)
        {
            if (index >= values.Length)
            {
                return 0;
            }

            if (values[index] > 0)
            {
                return 1
                    + DataDependentBranchingRecursion(values, index + 1)
                    + DataDependentBranchingRecursion(values, index + 1);
            }

            return 1 + DataDependentBranchingRecursion(values, index + 1);
        }

        // ---------------------------------------------------------------------
        // 24. Object graph topology is an unstated invariant.
        // Suggested result: NON_TERMINATION_RISK unless acyclicity is proven/declared.
        // A Node static type does not guarantee a finite acyclic linked list.
        // ---------------------------------------------------------------------
        public sealed class Node
        {
            public Node? Next { get; set; }
        }

        public static int CountLinkedNodes(Node? node)
        {
            int count = 0;
            while (node is not null)
            {
                count++;
                node = node.Next;
            }

            return count;
        }

        // ---------------------------------------------------------------------
        // 25. lock: local instruction count is constant, elapsed wait time is controlled by other threads.
        // Suggested result: distinguish algorithmic work from wall-clock/blocking time.
        // ---------------------------------------------------------------------
        private static readonly object Gate = new object();

        public static int LockWaitIsExternal(int value)
        {
            lock (Gate)
            {
                return value + 1;
            }
        }

        public static void HoldGateFor(TimeSpan duration)
        {
            lock (Gate)
            {
                Thread.Sleep(duration);
            }
        }

        // ---------------------------------------------------------------------
        // 26. Cache/history dependence.
        // Suggested result: RANGE/amortized/history-dependent.
        // The first call for n performs O(n) work; a later call for the same n is a dictionary lookup.
        // ---------------------------------------------------------------------
        private static readonly Dictionary<int, long> SumCache = new Dictionary<int, long>();

        public static long CacheDependentSum(int n)
        {
            if (SumCache.TryGetValue(n, out long cached))
            {
                return cached;
            }

            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += i;
            }

            SumCache[n] = sum;
            return sum;
        }

        // ---------------------------------------------------------------------
        // 27. Stream abstraction: concrete implementation can be memory, file, network, crypto,
        // compression, a custom stream, etc. The same virtual call site has different local and I/O cost.
        // Suggested result: INCONCLUSIVE for elapsed time; virtual-call summary needed for work complexity.
        // ---------------------------------------------------------------------
        public static int StreamRead(Stream stream, byte[] buffer)
        {
            return stream.Read(buffer, 0, buffer.Length);
        }

        // ---------------------------------------------------------------------
        // 28. Parallel loop: "time complexity" is underspecified.
        // Suggested result: report work separately from span/elapsed time.
        // Total work composes n iterations with callback cost; elapsed time depends on scheduling,
        // available processors, contention, and callback behavior.
        // ---------------------------------------------------------------------
        public static void ParallelLoop(int n, Action<int> body)
        {
            Parallel.For(0, n, body);
        }

        // ---------------------------------------------------------------------
        // 29. Repeated immutable-string concatenation.
        // Suggested result: DERIVABLE_WITH_SUMMARIES, but use total character count as an input dimension.
        // Counting only loop iterations misses repeated copying/allocation of growing strings.
        // ---------------------------------------------------------------------
        public static string RepeatedStringConcatenation(IEnumerable<string> parts)
        {
            string result = string.Empty;
            foreach (string part in parts)
            {
                result += part;
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // 30. Build-configuration dependence.
        // Suggested result: CONTEXT_DEPENDENT on preprocessor symbols / Compilation options.
        // A Roslyn SemanticModel represents the active compilation; another build configuration can
        // legitimately contain a different algorithm under the same method name.
        // ---------------------------------------------------------------------
#if COMPLEXITY_SLOW
        public static int BuildConfigurationDependent(int n)
        {
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    count++;
                }
            }

            return count;
        }
#else
        public static int BuildConfigurationDependent(int n)
        {
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                count++;
            }

            return count;
        }
#endif

        // ---------------------------------------------------------------------
        // 31. BFS worklist with no visit mark can spin on a cycle.
        // Suggested result: NON_TERMINATION_RISK / INCONCLUSIVE.
        // ---------------------------------------------------------------------
        public static int BfsNoVisited(List<int>[] graph, int start)
        {
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int count = 0;
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                count++;
                foreach (int next in graph[node])
                    queue.Enqueue(next);
            }

            return count;
        }
    }
}
