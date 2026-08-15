using ComplexityAnalyzer.Server;

namespace ComplexityAnalyzer.Tests;

public class ProjectWorkspaceTests
{
    [Fact]
    public async Task Fast_UsesProjectDefinesWhenReady()
    {
        var root = CreateProject();
        try
        {
            var service = new AnalyzerService();
            var project = Path.Combine(root, "Lib.csproj");
            await service.SetSolutionContext(
                new SetSolutionContextRequest(project));
            var path = Path.Combine(root, "Use.cs");
            var text = await File.ReadAllTextAsync(path);
            var response = await service.Analyze(new AnalyzeRequest(
                new Uri(path).AbsoluteUri, text, 1, "fast"));
            var fn = Assert.Single(
                response.Functions, f => f.Name == "Work");
            Assert.Equal("O(n²)", fn.Time);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The XML solution format, which MSBuildWorkspace has loaded
    /// since Roslyn 5.0. Picking up the project's <c>DefineConstants</c>
    /// is the proof it really opened the solution rather than quietly
    /// falling back to ad-hoc compilation.
    /// </summary>
    [Fact]
    public async Task Fast_UsesSlnxSolution()
    {
        var root = CreateProject();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.slnx"),
                """
                <Solution>
                  <Project Path="Lib.csproj" />
                </Solution>
                """);

            var service = new AnalyzerService();
            await service.SetSolutionContext(
                new SetSolutionContextRequest(
                    Path.Combine(root, "App.slnx")));

            var path = Path.Combine(root, "Use.cs");
            var text = await File.ReadAllTextAsync(path);
            var response = await service.Analyze(new AnalyzeRequest(
                new Uri(path).AbsoluteUri, text, 1, "fast"));

            var fn = Assert.Single(
                response.Functions, f => f.Name == "Work");
            Assert.Equal("O(n²)", fn.Time);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("App.sln", true)]
    [InlineData("App.slnx", true)]
    [InlineData("App.SLNX", true)]
    [InlineData("Lib.csproj", false)]
    public void IsSolution_AcceptsBothFormats(string path, bool expected) =>
        Assert.Equal(expected, DeepWorkspace.IsSolution(path));

    [Fact]
    public async Task Fast_WithoutProject_MissesDefine()
    {
        var service = new AnalyzerService();
        var response = await service.Analyze(new AnalyzeRequest(
            "file:///tmp/Use.cs",
            WorkSource(),
            1,
            "fast"));
        var fn = Assert.Single(
            response.Functions, f => f.Name == "Work");
        Assert.Equal("O(1)", fn.Time);
    }

    private static string CreateProject()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ohno-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <DefineConstants>PROJECT</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Use.cs"), WorkSource());
        return root;
    }

    private static string WorkSource() => """
        public static class Use
        {
            public static int Work(int n)
            {
                var total = 0;
        #if PROJECT
                for (var i = 0; i < n; i++)
                    for (var j = 0; j < n; j++)
                        total++;
        #else
                total++;
        #endif
                return total;
            }
        }
        """;
}
