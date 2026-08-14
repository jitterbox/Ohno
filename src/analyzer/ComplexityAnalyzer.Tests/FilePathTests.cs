using ComplexityAnalyzer.Server;
using Xunit;

namespace ComplexityAnalyzer.Tests;

public class FilePathTests
{
    [Fact]
    public void FromUri_RoundTripsANativePath()
    {
        var native = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "OhnoRoundTrip.cs"));
        var uri = new Uri(native).AbsoluteUri;
        Assert.True(FilePaths.Equal(native, FilePaths.FromUri(uri)));
    }

    [Fact]
    public void Equal_TreatsTheSameFullPathAsEqual()
    {
        var path = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "OhnoSame.cs"));
        Assert.True(FilePaths.Equal(path, path));
    }

    [Fact]
    public void FromUri_LeavesPlainPathsUsable()
    {
        var path = Path.Combine(Path.GetTempPath(), "plain.cs");
        var normalized = FilePaths.FromUri(path);
        Assert.False(string.IsNullOrWhiteSpace(normalized));
        Assert.Contains("plain.cs", normalized, StringComparison.OrdinalIgnoreCase);
    }
}
