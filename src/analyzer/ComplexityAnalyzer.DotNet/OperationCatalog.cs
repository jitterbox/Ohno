namespace ComplexityAnalyzer.DotNet;

/// <summary>
/// Extensible catalog of known .NET / LINQ operation costs, keyed by a
/// stable symbol identity (containing type + member + arity).
/// </summary>
/// <remarks>
/// Entries are summaries, not measurements. <c>Expected</c> and
/// <c>Amortized</c> kinds cap confidence at Medium.
/// <c>Enumerable.Repeat</c>
/// (<see href="https://learn.microsoft.com/dotnet/api/system.linq.enumerable.repeat">Repeat</see>)
/// is deferred O(1) to construct; <c>string.Concat</c> of that sequence
/// materializes payload size (element length × count).
/// Missing members fall through to primitive/unknown handling.
/// </remarks>
public sealed class OperationCatalog
{
    private readonly Dictionary<string, CatalogEntry> _entries = new(
        StringComparer.Ordinal);

    public static OperationCatalog CreateDefault()
    {
        var catalog = new OperationCatalog();
        catalog.RegisterArrays();
        catalog.RegisterString();
        catalog.RegisterList();
        catalog.RegisterDictionary();
        catalog.RegisterHashSet();
        catalog.RegisterQueue();
        catalog.RegisterStack();
        catalog.RegisterLinkedList();
        catalog.RegisterPriorityQueue();
        catalog.RegisterSorted();
        catalog.RegisterStringBuilder();
        catalog.RegisterImmutable();
        catalog.RegisterConcurrent();
        catalog.RegisterSpans();
        catalog.RegisterFrozen();
        catalog.RegisterRegex();
        catalog.RegisterLinq();
        return catalog;
    }

    public bool TryGet(string key, out CatalogEntry entry) =>
        _entries.TryGetValue(key, out entry!);

    public void Add(CatalogEntry entry) => _entries[entry.Key] = entry;

    public static string Key(string type, string member, int arity) =>
        $"{type}#{member}#{arity}";

    private void Method(
        string type,
        string member,
        int arity,
        SizeKind time,
        CostKind kind = CostKind.Exact,
        SizeKind space = SizeKind.Constant,
        int timePower = 1,
        bool deferred = false,
        bool materializes = false,
        bool sorts = false,
        bool queryable = false,
        SizeDeltaKind delta = SizeDeltaKind.None)
    {
        Add(new CatalogEntry(
            Key(type, member, arity),
            new CostTemplate(time, timePower),
            new CostTemplate(space),
            kind,
            deferred,
            materializes,
            sorts,
            queryable,
            delta));
    }

    private void RegisterArrays()
    {
        const string arr = "System.Array";
        Method(arr, "get_Length", 0, SizeKind.Constant);
        Method(arr, "Empty", 0, SizeKind.Constant);
        Method(arr, "Sort", 1, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(arr, "Sort", 2, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(arr, "Sort", 3, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(arr, "Sort", 4, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(arr, "BinarySearch", 2, SizeKind.LogReceiver);
        Method(arr, "IndexOf", 2, SizeKind.Receiver);
        Method(arr, "LastIndexOf", 2, SizeKind.Receiver);
        Method(arr, "Exists", 2, SizeKind.Receiver);
        Method(arr, "Find", 2, SizeKind.Receiver);
        Method(arr, "FindAll", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Method(arr, "CopyTo", 2, SizeKind.Receiver);
        Method(arr, "Clear", 3, SizeKind.Receiver);
        Method(arr, "Resize", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Method(arr, "Fill", 2, SizeKind.Receiver);
        Method(arr, "Fill", 4, SizeKind.Receiver);
        Method(arr, "Clone", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(arr, "Copy", 3, SizeKind.Receiver);
        Method(arr, "Copy", 5, SizeKind.Receiver);
        Method(arr, "ConstrainedCopy", 5, SizeKind.Receiver);
        Method(arr, "Reverse", 1, SizeKind.Receiver);
        Method(arr, "Reverse", 3, SizeKind.Receiver);
        Method(arr, "ConvertAll", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(arr, "FindIndex", 2, SizeKind.Receiver);
        Method(arr, "FindIndex", 3, SizeKind.Receiver);
        Method(arr, "FindIndex", 4, SizeKind.Receiver);
        Method(arr, "FindLast", 2, SizeKind.Receiver);
        Method(arr, "FindLastIndex", 2, SizeKind.Receiver);
        Method(arr, "TrueForAll", 2, SizeKind.Receiver);
        Method(arr, "ForEach", 2, SizeKind.Receiver);
        Method(arr, "BinarySearch", 3, SizeKind.LogReceiver);
        Method(arr, "BinarySearch", 4, SizeKind.LogReceiver);
        Method(arr, "AsReadOnly", 1, SizeKind.Constant);
        Method(arr, "GetLength", 1, SizeKind.Constant);
        Method("System.Buffer", "BlockCopy", 5, SizeKind.Receiver);
    }

    /// <summary>
    /// String members are linear in the receiver (or the argument) and
    /// allocate a new string when they materialize one. Without these,
    /// the most common calls in C# fell through to <c>C(name)</c>.
    /// </summary>
    private void RegisterString()
    {
        const string str = "System.String";
        Method(str, "get_Length", 0, SizeKind.Constant);
        Method(str, "get_Chars", 1, SizeKind.Constant);
        Method(str, "IsNullOrEmpty", 1, SizeKind.Constant);

        for (var arity = 1; arity <= 4; arity++)
        {
            Method(str, "Concat", arity, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
            Method(str, "Join", arity, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
            Method(str, "Format", arity, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
        }

        Method(str, "ToCharArray", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "ToCharArray", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);

        // Copies a slice; worst case is the whole receiver.
        Method(str, "Substring", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Substring", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Remove", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Remove", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Insert", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Replace", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "PadLeft", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "PadLeft", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "PadRight", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "PadRight", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);

        foreach (var name in new[]
        {
            "ToUpper", "ToLower", "ToUpperInvariant", "ToLowerInvariant",
            "Trim", "TrimStart", "TrimEnd", "Normalize",
        })
        {
            Method(str, name, 0, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
            Method(str, name, 1, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
        }

        // Split allocates one string per piece: linear in the source.
        for (var arity = 1; arity <= 3; arity++)
        {
            Method(str, "Split", arity, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
        }

        // Scans, no allocation. Naive search is O(n*m) worst case, but
        // the BCL uses a linear-time strategy for the common paths.
        foreach (var name in new[]
        {
            "IndexOf", "LastIndexOf", "IndexOfAny", "LastIndexOfAny",
            "Contains", "StartsWith", "EndsWith",
        })
        {
            for (var arity = 1; arity <= 3; arity++)
                Method(str, name, arity, SizeKind.Receiver);
        }

        for (var arity = 2; arity <= 6; arity++)
        {
            Method(str, "Compare", arity, SizeKind.Receiver);
            Method(str, "CompareOrdinal", arity, SizeKind.Receiver);
        }

        Method(str, "CompareTo", 1, SizeKind.Receiver);
        Method(str, "Equals", 1, SizeKind.Receiver);
        Method(str, "Equals", 2, SizeKind.Receiver);
        Method(str, "Equals", 3, SizeKind.Receiver);
        Method(str, "GetHashCode", 0, SizeKind.Receiver);
        Method(str, "CopyTo", 1, SizeKind.Receiver);
        Method(str, "CopyTo", 4, SizeKind.Receiver);
        Method(str, "AsSpan", 0, SizeKind.Constant);
        Method(str, "AsSpan", 1, SizeKind.Constant);
        Method(str, "AsSpan", 2, SizeKind.Constant);

        // string.ctor(char[]) / (char, count) copy their source.
        Method(str, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, ".ctor", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, ".ctor", 3, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
    }

    private void RegisterList()
    {
        // Read-only list contracts promise an O(1) indexer.
        foreach (var face in new[]
        {
            "System.Collections.Generic.IReadOnlyList`1",
            "System.Collections.Generic.IList`1",
        })
        {
            Method(face, "get_Count", 0, SizeKind.Constant);
            Method(face, "get_Item", 1, SizeKind.Constant);
        }

        const string list = "System.Collections.Generic.List`1";
        Method(list, "get_Count", 0, SizeKind.Constant);
        Method(list, "get_Item", 1, SizeKind.Constant);
        Method(list, "set_Item", 2, SizeKind.Constant);
        Method(list, "Add", 1, SizeKind.Constant, CostKind.Amortized,
            delta: SizeDeltaKind.Increment);
        Method(list, "AddRange", 1, SizeKind.Receiver,
            delta: SizeDeltaKind.Replace);
        Method(list, "Insert", 2, SizeKind.Receiver,
            delta: SizeDeltaKind.Increment);
        Method(list, "Remove", 1, SizeKind.Receiver,
            delta: SizeDeltaKind.Decrement);
        Method(list, "RemoveAt", 1, SizeKind.Receiver,
            delta: SizeDeltaKind.Decrement);
        Method(list, "Contains", 1, SizeKind.Receiver);
        Method(list, "IndexOf", 1, SizeKind.Receiver);
        Method(list, "Sort", 0, SizeKind.Receiver, sorts: true);
        Method(list, "Sort", 1, SizeKind.Receiver, sorts: true);
        Method(list, "BinarySearch", 1, SizeKind.LogReceiver);
        Method(list, "ToArray", 0, SizeKind.Receiver, space: SizeKind.Receiver);
        Method(list, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
        Method(list, "Find", 1, SizeKind.Receiver);
        Method(list, "Exists", 1, SizeKind.Receiver);
        Method(list, "FindIndex", 1, SizeKind.Receiver);
        Method(list, "FindLast", 1, SizeKind.Receiver);
        Method(list, "TrueForAll", 1, SizeKind.Receiver);
        Method(list, "ForEach", 1, SizeKind.Receiver);
        Method(list, "RemoveAll", 1, SizeKind.Receiver,
            delta: SizeDeltaKind.Decrement);
        Method(list, "RemoveRange", 2, SizeKind.Receiver,
            delta: SizeDeltaKind.Decrement);
        Method(list, "InsertRange", 2, SizeKind.Receiver,
            delta: SizeDeltaKind.Increment);
        Method(list, "Reverse", 0, SizeKind.Receiver);
        Method(list, "Reverse", 2, SizeKind.Receiver);
        Method(list, "CopyTo", 1, SizeKind.Receiver);
        Method(list, "CopyTo", 3, SizeKind.Receiver);
        Method(list, "GetRange", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(list, "TrimExcess", 0, SizeKind.Receiver);
        Method(list, "EnsureCapacity", 1, SizeKind.Receiver);
        Method(list, "get_Capacity", 0, SizeKind.Constant);
        // new List<T>(capacity) / (source): both allocate the argument.
        Method(list, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
        Method(
            "System.Collections.Generic.ICollection`1",
            "Add",
            1,
            SizeKind.Constant,
            CostKind.Amortized,
            delta: SizeDeltaKind.Increment);
    }

    private void RegisterDictionary()
    {
        // Read-only dictionary contracts promise an expected O(1) indexer.
        foreach (var face in new[]
        {
            "System.Collections.Generic.IReadOnlyDictionary`2",
            "System.Collections.Generic.IDictionary`2",
        })
        {
            Method(face, "get_Count", 0, SizeKind.Constant);
            Method(face, "get_Item", 1, SizeKind.Constant, CostKind.Expected);
            Method(face, "TryGetValue", 2, SizeKind.Constant, CostKind.Expected);
            Method(face, "ContainsKey", 1, SizeKind.Constant, CostKind.Expected);
        }

        const string dict = "System.Collections.Generic.Dictionary`2";
        Method(dict, "get_Count", 0, SizeKind.Constant);
        Method(dict, "get_Item", 1, SizeKind.Constant, CostKind.Expected);
        Method(dict, "set_Item", 2, SizeKind.Constant, CostKind.Expected);
        Method(dict, "TryGetValue", 2, SizeKind.Constant, CostKind.Expected);
        Method(dict, "ContainsKey", 1, SizeKind.Constant, CostKind.Expected);
        Method(dict, "ContainsValue", 1, SizeKind.Receiver);
        Method(dict, "Add", 2, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Increment);
        Method(dict, "Remove", 1, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Decrement);
        Method(dict, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
        Method(dict, "TryAdd", 2, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Increment);
        Method(dict, "get_Keys", 0, SizeKind.Constant);
        Method(dict, "get_Values", 0, SizeKind.Constant);
        Method(dict, "EnsureCapacity", 1, SizeKind.Receiver);
        Method(dict, "TrimExcess", 0, SizeKind.Receiver);
        Method(dict, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
        Method(
            "System.Collections.Generic.CollectionExtensions",
            "GetValueOrDefault",
            2,
            SizeKind.Constant,
            CostKind.Expected);
        Method(
            "System.Collections.Generic.CollectionExtensions",
            "GetValueOrDefault",
            3,
            SizeKind.Constant,
            CostKind.Expected);
    }

    private void RegisterHashSet()
    {
        const string set = "System.Collections.Generic.HashSet`1";
        Method(set, "get_Count", 0, SizeKind.Constant);
        Method(set, "Add", 1, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Increment);
        Method(set, "Contains", 1, SizeKind.Constant, CostKind.Expected);
        Method(set, "Remove", 1, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Decrement);
        Method(set, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
        Method(set, "TryGetValue", 2, SizeKind.Constant, CostKind.Expected);
        Method(set, "EnsureCapacity", 1, SizeKind.Receiver);
        Method(set, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
        // Bulk set operations walk both sides.
        foreach (var name in new[]
        {
            "UnionWith", "IntersectWith", "ExceptWith",
            "SymmetricExceptWith", "IsSubsetOf", "IsSupersetOf",
            "IsProperSubsetOf", "IsProperSupersetOf", "Overlaps",
            "SetEquals",
        })
        {
            Method(set, name, 1, SizeKind.Receiver,
                delta: name.EndsWith("With", StringComparison.Ordinal)
                    ? SizeDeltaKind.Increment
                    : SizeDeltaKind.None);
        }
    }

    private void RegisterQueue()
    {
        const string q = "System.Collections.Generic.Queue`1";
        Method(q, "get_Count", 0, SizeKind.Constant);
        Method(q, "Enqueue", 1, SizeKind.Constant, CostKind.Amortized,
            delta: SizeDeltaKind.Increment);
        Method(q, "Dequeue", 0, SizeKind.Constant,
            delta: SizeDeltaKind.Decrement);
        Method(q, "TryDequeue", 1, SizeKind.Constant,
            delta: SizeDeltaKind.Decrement);
        Method(q, "Peek", 0, SizeKind.Constant);
        Method(q, "TryPeek", 1, SizeKind.Constant);
        Method(q, "Contains", 1, SizeKind.Receiver);
        Method(q, "ToArray", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(q, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
        Method(q, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
    }

    private void RegisterStack()
    {
        const string s = "System.Collections.Generic.Stack`1";
        Method(s, "get_Count", 0, SizeKind.Constant);
        Method(s, "Push", 1, SizeKind.Constant, CostKind.Amortized,
            delta: SizeDeltaKind.Increment);
        Method(s, "Pop", 0, SizeKind.Constant,
            delta: SizeDeltaKind.Decrement);
        Method(s, "TryPop", 1, SizeKind.Constant,
            delta: SizeDeltaKind.Decrement);
        Method(s, "Peek", 0, SizeKind.Constant);
        Method(s, "TryPeek", 1, SizeKind.Constant);
        Method(s, "Contains", 1, SizeKind.Receiver);
        Method(s, "ToArray", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(s, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
        Method(s, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
    }

    private void RegisterLinkedList()
    {
        const string ll = "System.Collections.Generic.LinkedList`1";
        Method(ll, "get_Count", 0, SizeKind.Constant);
        Method(ll, "AddFirst", 1, SizeKind.Constant);
        Method(ll, "AddLast", 1, SizeKind.Constant);
        Method(ll, "Remove", 1, SizeKind.Receiver);
        Method(ll, "Find", 1, SizeKind.Receiver);
    }

    private void RegisterPriorityQueue()
    {
        const string pq = "System.Collections.Generic.PriorityQueue`2";
        Method(pq, "get_Count", 0, SizeKind.Constant);
        Method(pq, ".ctor", 1, SizeKind.Receiver, space: SizeKind.Receiver,
            delta: SizeDeltaKind.Replace);
        Method(pq, "Enqueue", 2, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Increment);
        Method(pq, "EnqueueRange", 1, SizeKind.Receiver,
            delta: SizeDeltaKind.Replace);
        Method(pq, "Dequeue", 0, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
        Method(pq, "TryDequeue", 2, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
        Method(pq, "Peek", 0, SizeKind.Constant);
        Method(pq, "EnqueueDequeue", 2, SizeKind.LogReceiver);
        Method(pq, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
    }

    private void RegisterSorted()
    {
        const string set = "System.Collections.Generic.SortedSet`1";
        Method(set, "get_Count", 0, SizeKind.Constant);
        Method(set, "Add", 1, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Increment);
        Method(set, "Remove", 1, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
        Method(set, "Contains", 1, SizeKind.LogReceiver);
        Method(set, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);

        const string dict = "System.Collections.Generic.SortedDictionary`2";
        Method(dict, "get_Count", 0, SizeKind.Constant);
        Method(dict, "get_Item", 1, SizeKind.LogReceiver);
        Method(dict, "Add", 2, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Increment);
        Method(dict, "Remove", 1, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
        Method(dict, "ContainsKey", 1, SizeKind.LogReceiver);
        Method(dict, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);

        const string sl = "System.Collections.Generic.SortedList`2";
        Method(sl, "get_Count", 0, SizeKind.Constant);
        Method(sl, "get_Item", 1, SizeKind.LogReceiver);
    }

    private void RegisterStringBuilder()
    {
        const string sb = "System.Text.StringBuilder";
        Method(sb, "get_Length", 0, SizeKind.Constant);
        Method(sb, "Append", 1, SizeKind.Constant, CostKind.Amortized,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "Append", 2, SizeKind.Constant, CostKind.Amortized,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "ToString", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(sb, "ToString", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(sb, "AppendLine", 0, SizeKind.Constant, CostKind.Amortized,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "AppendLine", 1, SizeKind.Constant, CostKind.Amortized,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "AppendJoin", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "AppendFormat", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(sb, "Insert", 2, SizeKind.Receiver,
            delta: SizeDeltaKind.Increment);
        Method(sb, "Remove", 2, SizeKind.Receiver,
            delta: SizeDeltaKind.Decrement);
        Method(sb, "Replace", 2, SizeKind.Receiver);
        Method(sb, "Clear", 0, SizeKind.Constant,
            delta: SizeDeltaKind.Clear);
        Method(sb, "get_Capacity", 0, SizeKind.Constant);
        Method(sb, "get_Chars", 1, SizeKind.Constant);
        Method(sb, ".ctor", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Replace);
    }

    /// <summary>
    /// Span / Memory helpers. These are the vectorized primitives real
    /// hot paths use, and every one of them was falling through to an
    /// invented O(1) before the catalog covered them.
    /// </summary>
    private void RegisterSpans()
    {
        const string ext = "System.MemoryExtensions";
        foreach (var name in new[]
        {
            "IndexOf", "LastIndexOf", "IndexOfAny", "LastIndexOfAny",
            "IndexOfAnyExcept", "Contains", "ContainsAny",
            "SequenceEqual", "SequenceCompareTo", "StartsWith",
            "EndsWith", "CopyTo", "TryCopyTo", "Fill", "Clear",
            "Reverse", "Count", "CommonPrefixLength", "Trim",
            "TrimStart", "TrimEnd", "Replace", "Split",
        })
        {
            for (var arity = 1; arity <= 4; arity++)
                Method(ext, name, arity, SizeKind.Receiver);
        }

        Method(ext, "Sort", 1, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(ext, "Sort", 2, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(ext, "Sort", 3, SizeKind.Receiver, timePower: 0, sorts: true);
        Method(ext, "BinarySearch", 2, SizeKind.LogReceiver);
        Method(ext, "BinarySearch", 3, SizeKind.LogReceiver);
        Method(ext, "AsSpan", 1, SizeKind.Constant);
        Method(ext, "AsSpan", 2, SizeKind.Constant);
        Method(ext, "AsSpan", 3, SizeKind.Constant);
        Method(ext, "AsMemory", 1, SizeKind.Constant);

        foreach (var span in new[]
        {
            "System.Span`1", "System.ReadOnlySpan`1",
            "System.Memory`1", "System.ReadOnlyMemory`1",
        })
        {
            Method(span, "get_Length", 0, SizeKind.Constant);
            Method(span, "get_IsEmpty", 0, SizeKind.Constant);
            Method(span, "get_Item", 1, SizeKind.Constant);
            Method(span, "get_Span", 0, SizeKind.Constant);
            Method(span, "Slice", 1, SizeKind.Constant);
            Method(span, "Slice", 2, SizeKind.Constant);
            Method(span, "CopyTo", 1, SizeKind.Receiver);
            Method(span, "TryCopyTo", 1, SizeKind.Receiver);
            Method(span, "Fill", 1, SizeKind.Receiver);
            Method(span, "Clear", 0, SizeKind.Receiver);
            Method(span, "ToArray", 0, SizeKind.Receiver,
                space: SizeKind.Receiver, materializes: true);
        }

        // SearchValues: O(n) build, then constant-time membership.
        const string sv = "System.Buffers.SearchValues";
        Method(sv, "Create", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method("System.Buffers.SearchValues`1", "Contains", 1,
            SizeKind.Constant);
    }

    /// <summary>
    /// Constructing a regex compiles the pattern, which is linear in
    /// the <em>pattern</em> — normally a source literal, and so Θ(1) in
    /// the input dimensions. A pattern built at runtime resolves to
    /// that value's size instead. Matching is deliberately absent:
    /// the backtracking engine's cost is not a function of size alone,
    /// and the non-backtracking engine is handled by
    /// <c>RegexFacts</c>.
    /// </summary>
    private void RegisterRegex()
    {
        const string regex = "System.Text.RegularExpressions.Regex";
        for (var arity = 1; arity <= 3; arity++)
        {
            Method(regex, ".ctor", arity, SizeKind.Receiver,
                space: SizeKind.Receiver);
        }

        Method(regex, "get_Options", 0, SizeKind.Constant);
        Method(regex, "get_RightToLeft", 0, SizeKind.Constant);
        Method(regex, "Escape", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(regex, "Unescape", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
    }

    /// <summary>
    /// Frozen collections trade an expensive build for the fastest
    /// possible reads — exactly the trade a complexity view should
    /// make visible rather than hide.
    /// </summary>
    private void RegisterFrozen()
    {
        const string ext = "System.Collections.Frozen.FrozenDictionary";
        Method(ext, "ToFrozenDictionary", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(ext, "ToFrozenDictionary", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(ext, "ToFrozenDictionary", 3, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method("System.Collections.Frozen.FrozenSet", "ToFrozenSet", 1,
            SizeKind.Receiver, space: SizeKind.Receiver,
            materializes: true);
        Method("System.Collections.Frozen.FrozenSet", "ToFrozenSet", 2,
            SizeKind.Receiver, space: SizeKind.Receiver,
            materializes: true);

        const string fd = "System.Collections.Frozen.FrozenDictionary`2";
        Method(fd, "get_Count", 0, SizeKind.Constant);
        Method(fd, "get_Item", 1, SizeKind.Constant);
        Method(fd, "TryGetValue", 2, SizeKind.Constant);
        Method(fd, "ContainsKey", 1, SizeKind.Constant);

        const string fs = "System.Collections.Frozen.FrozenSet`1";
        Method(fs, "get_Count", 0, SizeKind.Constant);
        Method(fs, "Contains", 1, SizeKind.Constant);
    }

    private void RegisterImmutable()
    {
        const string list = "System.Collections.Immutable.ImmutableList`1";
        Method(list, "Add", 1, SizeKind.LogReceiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(list, "Remove", 1, SizeKind.LogReceiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Decrement);
        Method(list, "get_Count", 0, SizeKind.Constant);
        Method(list, "get_Item", 1, SizeKind.Receiver);

        const string array = "System.Collections.Immutable.ImmutableArray`1";
        Method(array, "get_Length", 0, SizeKind.Constant);
        Method(array, "get_Item", 1, SizeKind.Constant);

        // ImmutableDictionary is an AVL tree: lookups are O(log n).
        const string dict = "System.Collections.Immutable.ImmutableDictionary`2";
        Method(dict, "get_Count", 0, SizeKind.Constant);
        Method(dict, "get_Item", 1, SizeKind.LogReceiver);
        Method(dict, "TryGetValue", 2, SizeKind.LogReceiver);
        Method(dict, "ContainsKey", 1, SizeKind.LogReceiver);
        Method(dict, "Add", 2, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Increment);
        Method(dict, "Remove", 1, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
    }

    private void RegisterConcurrent()
    {
        // Striped locking keeps reads effectively O(1) expected.
        const string dict = "System.Collections.Concurrent.ConcurrentDictionary`2";
        Method(dict, "get_Count", 0, SizeKind.Receiver, CostKind.Amortized);
        Method(dict, "get_Item", 1, SizeKind.Constant, CostKind.Expected);
        Method(dict, "set_Item", 2, SizeKind.Constant, CostKind.Expected);
        Method(dict, "TryGetValue", 2, SizeKind.Constant, CostKind.Expected);
        Method(dict, "ContainsKey", 1, SizeKind.Constant, CostKind.Expected);
        Method(dict, "TryAdd", 2, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Increment);
        Method(dict, "TryRemove", 2, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Decrement);
        Method(dict, "GetOrAdd", 2, SizeKind.Constant, CostKind.Expected,
            delta: SizeDeltaKind.Increment);
    }

    private void RegisterLinq()
    {
        RegisterEnumerable("System.Linq.Enumerable", queryable: false);
        RegisterEnumerable("System.Linq.Queryable", queryable: true);
    }

    private void RegisterEnumerable(string type, bool queryable)
    {
        void Linq(
            string name,
            int arity,
            SizeKind time = SizeKind.Receiver,
            bool deferred = true,
            bool materializes = false,
            bool sorts = false,
            SizeKind space = SizeKind.Constant)
        {
            Method(
                type,
                name,
                arity,
                time,
                queryable ? CostKind.Expected : CostKind.Exact,
                space,
                timePower: 1,
                deferred,
                materializes,
                sorts,
                queryable);
        }

        // Deferred, streaming: building the query is constant; the
        // per-element cost is paid by whatever enumerates it.
        foreach (var name in new[]
        {
            "Where", "Select", "Take", "TakeWhile", "Skip", "SkipWhile",
            "Cast", "OfType", "Prepend", "Append", "DefaultIfEmpty",
            "Index",
        })
        {
            Linq(name, 1);
            Linq(name, 2);
        }

        Linq("SelectMany", 2, SizeKind.Receiver);
        Linq("SelectMany", 3, SizeKind.Receiver);
        Linq("TakeLast", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Linq("SkipLast", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Linq("Zip", 2, SizeKind.Receiver);
        Linq("Zip", 3, SizeKind.Receiver);
        Linq("Concat", 2, SizeKind.Receiver);

        // Sorting operators. `sorts` makes the walker charge n log n
        // even when the pipeline hides the comparer in an overload.
        foreach (var name in new[]
        {
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        })
        {
            Linq(name, 2, SizeKind.Receiver, sorts: true);
            Linq(name, 3, SizeKind.Receiver, sorts: true);
        }

        // .NET 9 added parameterless Order/OrderDescending.
        Linq("Order", 1, SizeKind.Receiver, sorts: true);
        Linq("Order", 2, SizeKind.Receiver, sorts: true);
        Linq("OrderDescending", 1, SizeKind.Receiver, sorts: true);
        Linq("OrderDescending", 2, SizeKind.Receiver, sorts: true);
        Linq("Reverse", 1, SizeKind.Receiver, space: SizeKind.Receiver);

        // Hash-backed set operators: linear, and they retain a set.
        foreach (var name in new[]
        {
            "Distinct", "DistinctBy", "Union", "UnionBy", "Except",
            "ExceptBy", "Intersect", "IntersectBy", "GroupBy",
            "CountBy", "AggregateBy", "Chunk",
        })
        {
            for (var arity = 1; arity <= 4; arity++)
            {
                Linq(name, arity, SizeKind.Receiver,
                    space: SizeKind.Receiver);
            }
        }

        // Materializing operators: pay the source and retain a copy.
        foreach (var name in new[]
        {
            "ToList", "ToArray", "ToDictionary", "ToLookup", "ToHashSet",
        })
        {
            for (var arity = 1; arity <= 4; arity++)
            {
                Linq(name, arity, SizeKind.Receiver, deferred: false,
                    materializes: true, space: SizeKind.Receiver);
            }
        }

        // Eager scans: no allocation, linear worst case.
        foreach (var name in new[]
        {
            "Any", "All", "First", "FirstOrDefault", "Single",
            "SingleOrDefault", "Last", "LastOrDefault", "Count",
            "LongCount", "Contains", "Min", "Max", "MinBy", "MaxBy",
            "Sum", "Average", "Aggregate", "ElementAt",
            "ElementAtOrDefault", "SequenceEqual", "TryGetNonEnumeratedCount",
        })
        {
            for (var arity = 1; arity <= 4; arity++)
                Linq(name, arity, SizeKind.Receiver, deferred: false);
        }

        Linq("AsEnumerable", 1, SizeKind.Constant, deferred: true);
        Linq("Empty", 0, SizeKind.Constant, deferred: true);
        Linq("Repeat", 2, SizeKind.Constant, deferred: true);
        Linq("Range", 2, SizeKind.Constant, deferred: true);
    }
}
