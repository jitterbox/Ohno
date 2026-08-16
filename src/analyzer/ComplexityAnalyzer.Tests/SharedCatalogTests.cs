using ComplexityAnalyzer.DotNet;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// <c>src/shared/catalog.json</c> is the portable snapshot of
/// <see cref="OperationCatalog.CreateDefault"/>. Refresh with
/// <c>OHNO_WRITE_SHARED=1</c>.
/// </summary>
public class SharedCatalogTests
{
    [Fact]
    public void DefaultCatalog_MatchesSharedSnapshot()
    {
        var actual = CatalogJson.Serialize(
            OperationCatalog.CreateDefault());
        var path = SharedFiles.CatalogJson;
        if (Environment.GetEnvironmentVariable("OHNO_WRITE_SHARED")
            == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.True(
            File.Exists(path),
            "src/shared/catalog.json is missing. Run tests with "
            + "OHNO_WRITE_SHARED=1 to write it.");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SharedSnapshot_RoundTrips()
    {
        var original = OperationCatalog.CreateDefault();
        var json = CatalogJson.Serialize(original);
        var loaded = CatalogJson.Deserialize(json);
        Assert.Equal(
            original.Entries.Select(e => e.Key),
            loaded.Entries.Select(e => e.Key));
        Assert.Equal(json, CatalogJson.Serialize(loaded));
    }
}
