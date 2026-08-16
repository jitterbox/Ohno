using System.Text.Json;
using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Golden <see cref="ComplexityFormatter"/> vectors in
/// <c>src/shared/algebra-vectors.json</c>. A TypeScript port must
/// print the same <c>simplified</c> and <c>bigO</c> strings.
/// </summary>
public class AlgebraVectorTests
{
    public static TheoryData<string> VectorIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var vector in Load().Vectors)
                data.Add(vector.Id);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(VectorIds))]
    public void Vector_MatchesFormatter(string id)
    {
        var vector = Load().Vectors.First(v => v.Id == id);
        var expression = Parse(vector.Expr);
        var simplified = ComplexitySimplifier.Simplify(expression);
        Assert.Equal(
            vector.Simplified,
            ComplexityFormatter.Format(simplified));
        Assert.Equal(
            vector.BigO,
            ComplexityFormatter.FormatBigO(simplified));
    }

    [Fact]
    public void VectorIds_AreUnique()
    {
        var ids = Load().Vectors.Select(v => v.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    private static VectorFile Load()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(SharedFiles.AlgebraVectors));
        var root = document.RootElement;
        var vectors = root.GetProperty("vectors")
            .EnumerateArray()
            .Select(ReadVector)
            .ToArray();
        return new VectorFile(vectors);
    }

    private static Vector ReadVector(JsonElement element) => new(
        element.GetProperty("id").GetString()!,
        element.GetProperty("expr").Clone(),
        element.GetProperty("simplified").GetString()!,
        element.GetProperty("bigO").GetString()!);

    private static ComplexityExpression Parse(JsonElement element)
    {
        var op = element.GetProperty("op").GetString();
        return op switch
        {
            "const" => Cx.Constant(
                element.GetProperty("value").GetInt32()),
            "var" => Cx.Var(element.GetProperty("name").GetString()!),
            "log" => Cx.Log(Parse(element.GetProperty("inner"))),
            "factorial" => Cx.Factorial(
                Parse(element.GetProperty("inner"))),
            "pow" => Cx.Pow(
                Parse(element.GetProperty("base")),
                Parse(element.GetProperty("exp"))),
            "binomial" => Cx.Binomial(
                Parse(element.GetProperty("n")),
                Parse(element.GetProperty("k"))),
            "add" => Cx.Add(Args(element)),
            "mul" => Cx.Mul(Args(element)),
            "call" => Cx.Call(
                element.GetProperty("name").GetString()!),
            "unknown" => Cx.Unknown(
                element.GetProperty("reason").GetString() ?? ""),
            _ => throw new ArgumentOutOfRangeException(
                nameof(op), op, "Unknown algebra op."),
        };
    }

    private static ComplexityExpression[] Args(JsonElement element) =>
        element.GetProperty("args")
            .EnumerateArray()
            .Select(Parse)
            .ToArray();

    private sealed record VectorFile(Vector[] Vectors);

    private sealed record Vector(
        string Id,
        JsonElement Expr,
        string Simplified,
        string BigO);
}
