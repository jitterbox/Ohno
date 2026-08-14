using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Builds an ad-hoc <see cref="CSharpCompilation"/> from source text plus
/// trusted platform assemblies. Used by the fast analysis tier and tests.
/// </summary>
/// <remarks>
/// This is not a workspace. It has no project references, preprocessor
/// symbols from the real <c>.csproj</c>, or analyzer configuration.
/// Deep analysis uses
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.msbuild.msbuildworkspace">MSBuildWorkspace</see>
/// instead
/// (<see href="https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/work-with-workspace">Work with a workspace</see>).
/// Top-level statements compile as an exe so <c>&lt;Main$&gt;</c>
/// exists. Edge case: <c>#if</c> bodies follow this compilation's
/// symbols, not every solution configuration.
/// </remarks>
public static class CompilationFactory
{
    private static readonly IReadOnlyList<MetadataReference> Platform =
        LoadPlatformReferences();

    public const string GlobalsPath = "Ohno.GlobalUsings.cs";

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Collections.Immutable;
        global using System.IO;
        global using System.Linq;
        global using System.Text;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static readonly CSharpParseOptions Parse =
        new(LanguageVersion.Latest);

    public static CSharpCompilation Create(string source, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source, Parse, path: name + ".cs");
        var globals = CSharpSyntaxTree.ParseText(
            GlobalUsings, Parse, path: GlobalsPath);
        var kind = HasTopLevel(tree)
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;
        return CSharpCompilation.Create(
            name,
            new[] { globals, tree },
            Platform,
            new CSharpCompilationOptions(kind));
    }

    private static bool HasTopLevel(SyntaxTree tree) =>
        tree.GetRoot() is CompilationUnitSyntax unit
        && unit.Members.OfType<GlobalStatementSyntax>().Any();

    public static SyntaxTree SourceTree(Compilation compilation) =>
        compilation.SyntaxTrees.First(t => t.FilePath != GlobalsPath);

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
