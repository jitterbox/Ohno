using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Assigns symbolic variables to method parameters that look like
/// input sizes (collections and integral counts).
/// </summary>
/// <remarks>
/// A parameter named <c>k</c>, <c>count</c>, <c>size</c>, <c>limit</c>,
/// <c>take</c>, or <c>top</c> becomes k. Other integrals take the next
/// letter. Strings use <c>.Length</c>
/// (<see href="https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types#the-string-type">string</see>).
/// Independent dimensions are never collapsed (n + m stays n + m).
/// </remarks>
internal static class DimensionInferrer
{
    private static readonly string[] Letters =
        ["n", "m", "p", "q", "r", "s", "t", "u", "v", "w"];

    public static void Infer(IMethodSymbol method, AnalysisState state)
    {
        var next = 0;
        foreach (var parameter in method.Parameters)
            InferParameter(parameter, state, ref next);
        if (method.MethodKind == MethodKind.Constructor) return;
        InferPrimary(method.ContainingType, state, ref next);
    }

    private static void InferParameter(
        IParameterSymbol parameter,
        AnalysisState state,
        ref int next)
    {
        if (state.Sizes.ContainsKey(parameter)) return;
        if (TryScalarDimension(parameter, state, ref next)) return;
        if (!IsCollection(parameter.Type)) return;
        var letter = LinkedListCountLetter(parameter.Type, state)
            ?? NextUnused(ref next, parameter.Name, state);
        var suffix = LengthSuffix(parameter.Type);
        var meaning = $"{parameter.Name}{suffix}";
        var expr = Cx.Var(letter);
        state.Dimensions.Add(new InputDimension(letter, meaning));
        state.Sizes[parameter] = expr;
    }

    private static void InferPrimary(
        INamedTypeSymbol? type, AnalysisState state, ref int next)
    {
        if (type is null) return;
        foreach (var parameter in PrimaryParameters(type))
        {
            InferParameter(parameter, state, ref next);
            AliasCaptures(type, parameter, state);
        }
    }

    private static IEnumerable<IParameterSymbol> PrimaryParameters(
        INamedTypeSymbol type)
    {
        foreach (var ctor in type.InstanceConstructors)
        {
            if (!IsPrimary(ctor)) continue;
            foreach (var parameter in ctor.Parameters)
                yield return parameter;
        }
    }

    private static bool IsPrimary(IMethodSymbol ctor)
    {
        foreach (var reference in ctor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax type
                && type.ParameterList is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void AliasCaptures(
        INamedTypeSymbol type,
        IParameterSymbol parameter,
        AnalysisState state)
    {
        if (!state.Sizes.TryGetValue(parameter, out var size)) return;
        foreach (var member in type.GetMembers(parameter.Name))
        {
            if (member is IFieldSymbol or IPropertySymbol)
                state.Sizes[member] = size;
        }
    }

    internal static ComplexityExpression Fresh(
        AnalysisState state, string meaning)
    {
        var used = state.Dimensions
            .Select(d => d.Variable)
            .ToHashSet();
        var letter = !used.Contains("k")
            ? "k"
            : Letters.FirstOrDefault(l => !used.Contains(l)) ?? "z";
        var expr = Cx.Var(letter);
        state.Dimensions.Add(new InputDimension(letter, meaning));
        return expr;
    }

    private static bool TryScalarDimension(
        IParameterSymbol parameter,
        AnalysisState state,
        ref int next)
    {
        if (!IsIntegral(parameter.Type)) return false;
        var name = parameter.Name;
        var letter = name.Length == 1
            ? name
            : IsCountName(name) ? "k" : NextLetter(ref next, name);
        if (state.Dimensions.Any(d => d.Variable == letter))
            return false;
        state.Dimensions.Add(
            new InputDimension(letter, $"parameter {name}"));
        state.Sizes[parameter] = Cx.Var(letter);
        return true;
    }

    private static string NextLetter(ref int index, string fallback) =>
        NextUnused(ref index, fallback, used: null);

    private static string NextUnused(
        ref int index,
        string fallback,
        AnalysisState state) =>
        NextUnused(ref index, fallback, state.Dimensions
            .Select(d => d.Variable)
            .ToHashSet());

    private static string NextUnused(
        ref int index,
        string fallback,
        HashSet<string>? used)
    {
        while (index < Letters.Length)
        {
            var letter = Letters[index++];
            if (used is null || !used.Contains(letter))
                return letter;
        }

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

    private static string? LinkedListCountLetter(
        ITypeSymbol type, AnalysisState state)
    {
        if (!IsLinkedListSequence(type)) return null;
        if (state.Dimensions.Any(d => d.Variable == "k")) return null;
        return "k";
    }

    internal static bool IsLinkedListSequence(ITypeSymbol? type)
    {
        var element = ElementType(type);
        return element is not null && HasSelfNext(element);
    }

    private static ITypeSymbol? ElementType(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (type is not INamedTypeSymbol named) return null;
        var enumerable = named.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition.SpecialType
                == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable?.TypeArguments.FirstOrDefault()
            ?? (named.TypeArguments.Length == 1
                ? named.TypeArguments[0]
                : null);
    }

    internal static bool HasSelfNext(ITypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (!member.Name.Equals("next", StringComparison.OrdinalIgnoreCase))
                continue;
            var memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol prop => prop.Type,
                _ => null,
            };
            if (memberType is not null && SameType(memberType, type))
                return true;
        }

        return false;
    }

    private static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(
            left.OriginalDefinition, right.OriginalDefinition);

    internal static bool IsCollection(ITypeSymbol? type)
    {
        if (type is null) return false;
        if (type is IArrayTypeSymbol) return true;
        if (type.SpecialType == SpecialType.System_String) return true;
        if (IsSpanLike(type)) return true;
        if (SymbolKeys.IsQueryable(type)) return true;
        return type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType
                is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T)
            || type.SpecialType
                is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    internal static bool IsSpanLike(ITypeSymbol? type)
    {
        var name = SymbolKeys.TypeName(type);
        return name is "System.Span`1"
            or "System.ReadOnlySpan`1"
            or "System.Memory`1"
            or "System.ReadOnlyMemory`1";
    }

    internal static string LengthSuffix(ITypeSymbol type) =>
        type is IArrayTypeSymbol
            || type.SpecialType == SpecialType.System_String
            || IsSpanLike(type)
            ? ".Length"
            : ".Count";
}
