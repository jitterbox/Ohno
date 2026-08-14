using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

internal static class DimensionInferrer
{
    private static readonly string[] Letters =
        ["n", "m", "p", "q", "r", "s", "t", "u", "v", "w"];

    public static void Infer(IMethodSymbol method, AnalysisState state)
    {
        var next = 0;
        foreach (var parameter in method.Parameters)
        {
            if (TryScalarDimension(parameter, state)) continue;
            if (IsCollection(parameter.Type))
            {
                var letter = NextLetter(ref next, parameter.Name);
                var suffix = LengthSuffix(parameter.Type);
                var meaning = $"{parameter.Name}{suffix}";
                var expr = Cx.Var(letter);
                state.Dimensions.Add(new InputDimension(letter, meaning));
                state.Sizes[parameter] = expr;
            }
        }
    }

    private static bool TryScalarDimension(
        IParameterSymbol parameter, AnalysisState state)
    {
        if (!IsIntegral(parameter.Type)) return false;
        var name = parameter.Name;
        if (name.Length != 1 && !IsCountName(name)) return false;
        var letter = name.Length == 1 ? name : "k";
        if (state.Dimensions.Any(d => d.Variable == letter)) return false;
        state.Dimensions.Add(new InputDimension(letter, $"parameter {name}"));
        state.Sizes[parameter] = Cx.Var(letter);
        return true;
    }

    private static string NextLetter(ref int index, string fallback)
    {
        if (index < Letters.Length) return Letters[index++];
        return fallback.Length > 0 ? fallback[0].ToString() : "n";
    }

    private static bool IsIntegral(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_UInt32
            or SpecialType.System_UInt64
            or SpecialType.System_Int16;

    private static bool IsCountName(string name) =>
        name.Equals("k", StringComparison.OrdinalIgnoreCase)
        || name.Equals("count", StringComparison.OrdinalIgnoreCase)
        || name.Equals("size", StringComparison.OrdinalIgnoreCase)
        || name.Equals("limit", StringComparison.OrdinalIgnoreCase)
        || name.Equals("take", StringComparison.OrdinalIgnoreCase)
        || name.Equals("top", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return true;
        if (SymbolKeys.IsQueryable(type)) return true;
        return type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType
                is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T)
            || type.SpecialType
                is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    internal static string LengthSuffix(ITypeSymbol type) =>
        type is IArrayTypeSymbol ? ".Length" : ".Count";
}
