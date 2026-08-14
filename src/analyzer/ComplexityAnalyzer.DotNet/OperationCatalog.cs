namespace ComplexityAnalyzer.DotNet;

/// <summary>
/// Extensible catalog of known .NET / LINQ operation costs, keyed by a
/// stable symbol identity (containing type + member + arity).
/// </summary>
public sealed class OperationCatalog
{
    private readonly Dictionary<string, CatalogEntry> _entries = new(
        StringComparer.Ordinal);

    public static OperationCatalog CreateDefault()
    {
        var catalog = new OperationCatalog();
        catalog.RegisterArrays();
        catalog.RegisterList();
        catalog.RegisterDictionary();
        catalog.RegisterHashSet();
        catalog.RegisterQueue();
        catalog.RegisterStack();
        catalog.RegisterLinkedList();
        catalog.RegisterPriorityQueue();
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
        bool queryable = false)
    {
        Add(new CatalogEntry(
            Key(type, member, arity),
            new CostTemplate(time, timePower),
            new CostTemplate(space),
            kind,
            deferred,
            materializes,
            sorts,
            queryable));
    }

    private void RegisterArrays()
    {
        const string arr = "System.Array";
        Method(arr, "get_Length", 0, SizeKind.Constant);
        Method(arr, "Sort", 1, SizeKind.Receiver, timePower: 0, sorts: true);
        // Sort is n log n — encoded as Sorts flag + Receiver size.
        Method(arr, "BinarySearch", 2, SizeKind.LogReceiver);
        Method(arr, "IndexOf", 2, SizeKind.Receiver);
        Method(arr, "LastIndexOf", 2, SizeKind.Receiver);
        Method(arr, "Exists", 2, SizeKind.Receiver);
        Method(arr, "Find", 2, SizeKind.Receiver);
        Method(arr, "FindAll", 2, SizeKind.Receiver, space: SizeKind.Receiver);
        Method(arr, "CopyTo", 2, SizeKind.Receiver);
        Method(arr, "Clear", 3, SizeKind.Receiver);
        Method(arr, "Resize", 2, SizeKind.Receiver, space: SizeKind.Receiver);
    }

    private void RegisterList()
    {
        const string list = "System.Collections.Generic.List`1";
        Method(list, "get_Count", 0, SizeKind.Constant);
        Method(list, "get_Item", 1, SizeKind.Constant);
        Method(list, "set_Item", 2, SizeKind.Constant);
        Method(list, "Add", 1, SizeKind.Constant, CostKind.Amortized);
        Method(list, "AddRange", 1, SizeKind.Receiver);
        Method(list, "Insert", 2, SizeKind.Receiver);
        Method(list, "Remove", 1, SizeKind.Receiver);
        Method(list, "RemoveAt", 1, SizeKind.Receiver);
        Method(list, "Contains", 1, SizeKind.Receiver);
        Method(list, "IndexOf", 1, SizeKind.Receiver);
        Method(list, "Sort", 0, SizeKind.Receiver, sorts: true);
        Method(list, "Sort", 1, SizeKind.Receiver, sorts: true);
        Method(list, "BinarySearch", 1, SizeKind.LogReceiver);
        Method(list, "ToArray", 0, SizeKind.Receiver, space: SizeKind.Receiver);
        Method(list, "Clear", 0, SizeKind.Receiver);
        Method(list, "Find", 1, SizeKind.Receiver);
        Method(list, "Exists", 1, SizeKind.Receiver);
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
        Method(dict, "Add", 2, SizeKind.Constant, CostKind.Expected);
        Method(dict, "Remove", 1, SizeKind.Constant, CostKind.Expected);
        Method(dict, "Clear", 0, SizeKind.Receiver);
    }

    private void RegisterHashSet()
    {
        const string set = "System.Collections.Generic.HashSet`1";
        Method(set, "get_Count", 0, SizeKind.Constant);
        Method(set, "Add", 1, SizeKind.Constant, CostKind.Expected);
        Method(set, "Contains", 1, SizeKind.Constant, CostKind.Expected);
        Method(set, "Remove", 1, SizeKind.Constant, CostKind.Expected);
        Method(set, "Clear", 0, SizeKind.Receiver);
    }

    private void RegisterQueue()
    {
        const string q = "System.Collections.Generic.Queue`1";
        Method(q, "get_Count", 0, SizeKind.Constant);
        Method(q, "Enqueue", 1, SizeKind.Constant, CostKind.Amortized);
        Method(q, "Dequeue", 0, SizeKind.Constant);
        Method(q, "Peek", 0, SizeKind.Constant);
        Method(q, "Clear", 0, SizeKind.Receiver);
    }

    private void RegisterStack()
    {
        const string s = "System.Collections.Generic.Stack`1";
        Method(s, "get_Count", 0, SizeKind.Constant);
        Method(s, "Push", 1, SizeKind.Constant, CostKind.Amortized);
        Method(s, "Pop", 0, SizeKind.Constant);
        Method(s, "Peek", 0, SizeKind.Constant);
        Method(s, "Clear", 0, SizeKind.Receiver);
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
        Method(pq, "Enqueue", 2, SizeKind.LogReceiver);
        Method(pq, "Dequeue", 0, SizeKind.LogReceiver);
        Method(pq, "Peek", 0, SizeKind.Constant);
        Method(pq, "EnqueueDequeue", 2, SizeKind.LogReceiver);
        Method(pq, "Clear", 0, SizeKind.Receiver);
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
    }
}
