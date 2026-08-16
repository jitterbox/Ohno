using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComplexityAnalyzer.DotNet;

/// <summary>
/// Portable catalog table. C# remains the writer
/// (<see cref="OperationCatalog.CreateDefault"/>); this codec is the
/// shared snapshot TS will load and CI asserts against.
/// </summary>
public static class CatalogJson
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(OperationCatalog catalog)
    {
        var document = new CatalogDocument
        {
            Version = Version,
            Entries = catalog.Entries.Select(ToRow).ToList(),
        };
        return JsonSerializer.Serialize(document, Options)
            .Replace("\r\n", "\n") + "\n";
    }

    public static OperationCatalog Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(
            json, Options)
            ?? throw new InvalidOperationException(
                "catalog.json deserialized to null.");
        var catalog = new OperationCatalog();
        foreach (var row in document.Entries)
            catalog.Add(FromRow(row));
        return catalog;
    }

    private static CatalogRow ToRow(CatalogEntry entry) => new()
    {
        Key = entry.Key,
        Time = ToTemplate(entry.Time),
        Space = ToTemplate(entry.Space),
        Kind = KindName(entry.Kind),
        Deferred = entry.Deferred,
        Materializes = entry.Materializes,
        Sorts = entry.Sorts,
        Queryable = entry.IsQueryable,
        Delta = DeltaName(entry.Delta),
    };

    private static CatalogEntry FromRow(CatalogRow row) => new(
        row.Key,
        FromTemplate(row.Time),
        FromTemplate(row.Space),
        ParseKind(row.Kind),
        row.Deferred,
        row.Materializes,
        row.Sorts,
        row.Queryable,
        ParseDelta(row.Delta));

    private static TemplateRow ToTemplate(CostTemplate template) =>
        new() { Size = SizeName(template.Size), Power = template.Power };

    private static CostTemplate FromTemplate(TemplateRow row) =>
        new(ParseSize(row.Size), row.Power);

    private static string SizeName(SizeKind size) => size switch
    {
        SizeKind.Constant => "constant",
        SizeKind.Receiver => "receiver",
        SizeKind.LogReceiver => "logReceiver",
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    private static SizeKind ParseSize(string size) => size switch
    {
        "constant" => SizeKind.Constant,
        "receiver" => SizeKind.Receiver,
        "logReceiver" => SizeKind.LogReceiver,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    private static string KindName(CostKind kind) => kind switch
    {
        CostKind.Exact => "exact",
        CostKind.Amortized => "amortized",
        CostKind.Expected => "expected",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CostKind ParseKind(string kind) => kind switch
    {
        "exact" => CostKind.Exact,
        "amortized" => CostKind.Amortized,
        "expected" => CostKind.Expected,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string DeltaName(SizeDeltaKind delta) => delta switch
    {
        SizeDeltaKind.None => "none",
        SizeDeltaKind.Increment => "increment",
        SizeDeltaKind.Decrement => "decrement",
        SizeDeltaKind.Clear => "clear",
        SizeDeltaKind.Replace => "replace",
        _ => throw new ArgumentOutOfRangeException(nameof(delta)),
    };

    private static SizeDeltaKind ParseDelta(string delta) => delta switch
    {
        "none" => SizeDeltaKind.None,
        "increment" => SizeDeltaKind.Increment,
        "decrement" => SizeDeltaKind.Decrement,
        "clear" => SizeDeltaKind.Clear,
        "replace" => SizeDeltaKind.Replace,
        _ => throw new ArgumentOutOfRangeException(nameof(delta), delta, null),
    };

    private sealed class CatalogDocument
    {
        public int Version { get; set; }
        public List<CatalogRow> Entries { get; set; } = [];
    }

    private sealed class CatalogRow
    {
        public string Key { get; set; } = "";
        public TemplateRow Time { get; set; } = new();
        public TemplateRow Space { get; set; } = new();
        public string Kind { get; set; } = "exact";
        public bool Deferred { get; set; }
        public bool Materializes { get; set; }
        public bool Sorts { get; set; }
        public bool Queryable { get; set; }
        public string Delta { get; set; } = "none";
    }

    private sealed class TemplateRow
    {
        public string Size { get; set; } = "constant";
        public int Power { get; set; } = 1;
    }
}
