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
    }

    private void RegisterString()
    {
        const string str = "System.String";
        Method(str, "get_Length", 0, SizeKind.Constant);
        Method(str, "ToCharArray", 0, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "ToCharArray", 2, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
        Method(str, "Concat", 1, SizeKind.Receiver,
            space: SizeKind.Receiver, materializes: true);
    }

    private void RegisterList()
    {
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
        Method(q, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
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
        Method(s, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
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
        Method(dict, "Add", 2, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Increment);
        Method(dict, "Remove", 1, SizeKind.LogReceiver,
            delta: SizeDeltaKind.Decrement);
        Method(dict, "ContainsKey", 1, SizeKind.LogReceiver);
        Method(dict, "Clear", 0, SizeKind.Receiver,
            delta: SizeDeltaKind.Clear);
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
    }

    private void RegisterImmutable()
    {
        const string list = "System.Collections.Immutable.ImmutableList`1";
        Method(list, "Add", 1, SizeKind.LogReceiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Increment);
        Method(list, "Remove", 1, SizeKind.LogReceiver,
            space: SizeKind.Receiver, delta: SizeDeltaKind.Decrement);
        Method(list, "get_Count", 0, SizeKind.Constant);
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

        Linq("Where", 2);
        Linq("Select", 2);
        Linq("SelectMany", 2, SizeKind.Receiver);
        Linq("OrderBy", 2, SizeKind.Receiver, sorts: true);
        Linq("OrderByDescending", 2, SizeKind.Receiver, sorts: true);
        Linq("ThenBy", 2, SizeKind.Receiver, sorts: true);
        Linq("ThenByDescending", 2, SizeKind.Receiver, sorts: true);
        Linq("GroupBy", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Linq("Distinct", 1, SizeKind.Receiver, space: SizeKind.Receiver);
        Linq("ToList", 1, SizeKind.Receiver, deferred: false,
            materializes: true, space: SizeKind.Receiver);
        Linq("ToArray", 1, SizeKind.Receiver, deferred: false,
            materializes: true, space: SizeKind.Receiver);
        Linq("ToDictionary", 2, SizeKind.Receiver, deferred: false,
            materializes: true, space: SizeKind.Receiver);
        Linq("ToLookup", 2, SizeKind.Receiver, deferred: false,
            materializes: true, space: SizeKind.Receiver);
        Linq("Any", 1, SizeKind.Receiver, deferred: false);
        Linq("Any", 2, SizeKind.Receiver, deferred: false);
        Linq("All", 2, SizeKind.Receiver, deferred: false);
        Linq("First", 1, SizeKind.Receiver, deferred: false);
        Linq("First", 2, SizeKind.Receiver, deferred: false);
        Linq("FirstOrDefault", 1, SizeKind.Receiver, deferred: false);
        Linq("FirstOrDefault", 2, SizeKind.Receiver, deferred: false);
        Linq("Single", 1, SizeKind.Receiver, deferred: false);
        Linq("Single", 2, SizeKind.Receiver, deferred: false);
        Linq("Count", 1, SizeKind.Receiver, deferred: false);
        Linq("Count", 2, SizeKind.Receiver, deferred: false);
        Linq("Contains", 2, SizeKind.Receiver, deferred: false);
        Linq("Min", 1, SizeKind.Receiver, deferred: false);
        Linq("Max", 1, SizeKind.Receiver, deferred: false);
        Linq("Sum", 1, SizeKind.Receiver, deferred: false);
        Linq("Aggregate", 2, SizeKind.Receiver, deferred: false);
        Linq("Aggregate", 3, SizeKind.Receiver, deferred: false);
        Linq("AsEnumerable", 1, SizeKind.Constant, deferred: true);
        Linq("Repeat", 2, SizeKind.Constant, deferred: true);
    }
}
