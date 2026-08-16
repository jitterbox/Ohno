using System.Text.Json;
using ComplexityAnalyzer.Server;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// <c>src/shared/protocol.schema.json</c> is the wire contract.
/// C# DTOs and the TypeScript types must cover the same fields.
/// </summary>
public class ProtocolSchemaTests
{
    private static readonly JsonNamingPolicy Camel =
        JsonNamingPolicy.CamelCase;

    [Fact]
    public void SchemaMethods_MatchKnownSet()
    {
        using var schema = LoadSchema();
        var methods = schema.RootElement.GetProperty("methods");
        var names = methods.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "initialize",
                "ohno/analyze",
                "ohno/analyzeDeep",
                "ohno/setSolutionContext",
                "shutdown",
            },
            names);
    }

    [Fact]
    public void DtoProperties_CoverSchemaRequiredFields()
    {
        using var schema = LoadSchema();
        var defs = schema.RootElement.GetProperty("definitions");
        AssertCovered<AnalyzeResponse>(defs, "AnalyzeResponse");
        AssertCovered<FunctionDto>(defs, "FunctionComplexity");
        AssertCovered<EvidenceDto>(defs, "EvidenceNode");
        AssertCovered<PatternDto>(defs, "RecognizedPattern");
        AssertCovered<ApproachDto>(defs, "AlgorithmApproach");
        AssertCovered<DimensionDto>(defs, "InputDimension");
        AssertCovered<WarningDto>(defs, "AnalysisWarning");
        AssertCovered<SuggestionDto>(defs, "BoundingSuggestion");
        AssertCovered<RangeDto>(defs, "LineRange");
    }

    private static void AssertCovered<T>(
        JsonElement defs, string definition)
    {
        var required = defs.GetProperty(definition)
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        var names = typeof(T).GetProperties()
            .Select(p => Camel.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);
        var missing = required.Where(r => !names.Contains(r)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"{typeof(T).Name} is missing schema fields on "
            + $"{definition}: {string.Join(", ", missing)}");
    }

    private static JsonDocument LoadSchema() =>
        JsonDocument.Parse(File.ReadAllText(SharedFiles.ProtocolSchema));
}
