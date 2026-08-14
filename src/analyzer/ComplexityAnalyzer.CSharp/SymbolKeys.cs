using ComplexityAnalyzer.DotNet;
using Microsoft.CodeAnalysis;

namespace ComplexityAnalyzer.CSharp;

internal static class SymbolKeys
{
    public static string? ForMethod(IMethodSymbol method)
    {
        var type = TypeName(method.ContainingType);
        return type is null
            ? null
            : OperationCatalog.Key(
                type, method.Name, method.Parameters.Length);
    }

    public static string? TypeName(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol) return "System.Array";
        if (type is not INamedTypeSymbol named) return null;
        var def = named.OriginalDefinition;
        var ns = def.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(ns) || ns == "<global namespace>")
            return def.MetadataName;
        return $"{ns}.{def.MetadataName}";
    }

    public static bool IsQueryable(ITypeSymbol? type)
    {
        var name = TypeName(type);
        return name is "System.Linq.IQueryable`1"
            or "System.Linq.IOrderedQueryable`1"
            or "System.Linq.IQueryable";
    }

    public static bool IsExpressionTree(ITypeSymbol? type)
    {
        var name = TypeName(type);
        return name is "System.Linq.Expressions.Expression`1"
            or "System.Linq.Expressions.Expression";
    }
}
