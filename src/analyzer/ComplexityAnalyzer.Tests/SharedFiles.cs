namespace ComplexityAnalyzer.Tests;

internal static class SharedFiles
{
    public static string Directory { get; } = FindShared();

    public static string ProtocolSchema =>
        Path.Combine(Directory, "protocol.schema.json");

    public static string CatalogJson =>
        Path.Combine(Directory, "catalog.json");

    public static string AlgebraVectors =>
        Path.Combine(Directory, "algebra-vectors.json");

    private static string FindShared()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName, "src", "shared");
            if (File.Exists(Path.Combine(
                    candidate, "protocol.schema.json")))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(
            "src/shared/protocol.schema.json not found.");
    }
}
