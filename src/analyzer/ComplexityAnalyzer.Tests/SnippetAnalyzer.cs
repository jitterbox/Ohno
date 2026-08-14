using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComplexityAnalyzer.Tests;

internal static class SnippetAnalyzer
{
    public static ComplexityResult Analyze(string methodSource)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public static class Snippet
            {
            {{methodSource}}
            }
            """;
        return AnalyzeNamed(source, firstMethod: true);
    }

    public static ComplexityResult AnalyzeNamed(
        string source, bool firstMethod = false, string? name = null)
    {
        var compilation = CompilationFactory.Create(source, "SnippetTests");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var methodNode = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => firstMethod || m.Identifier.Text == name);
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(methodNode)!;
        return new CSharpMethodAnalyzer()
            .Analyze(symbol, model, AnalysisTier.Fast);
    }
}
