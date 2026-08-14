using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Builds an ad-hoc C# compilation from source text plus trusted platform
/// assemblies. Used by the fast analysis tier and by tests.
/// </summary>
public static class CompilationFactory
{
    private static readonly IReadOnlyList<MetadataReference> Platform =
        LoadPlatformReferences();

    public static CSharpCompilation Create(string source, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            name,
            new[] { tree },
            Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyList<MetadataReference> LoadPlatformReferences()
    {
        var data = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(data))
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            };
        }

        return data
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }
}
