using ComplexityAnalyzer.Core;
using ComplexityAnalyzer.CSharp;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Which members appear as results at all.
/// </summary>
/// <remarks>
/// Accessors and operators were analyzed only when something called
/// them, so an expensive getter — one of the easiest places for an
/// O(n) to hide — had no result of its own. They are now first-class,
/// while auto-implemented accessors are skipped because there is no
/// body to cost.
/// </remarks>
public class MemberSurfaceTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        public sealed class Catalogue
        {
            private readonly int[] _values = new int[8];
            private readonly List<string> _names = new();

            // Auto-property: no body, so no result of its own.
            public string Title { get; set; } = "";

            // Expression-bodied property over a real scan.
            public int Total => _values.Sum();

            // Block-bodied getter that scans.
            public int Largest
            {
                get
                {
                    var best = int.MinValue;
                    foreach (var value in _values)
                    {
                        if (value > best) best = value;
                    }

                    return best;
                }
            }

            // Setter with real work.
            public string Newest
            {
                get => _names.Count == 0 ? "" : _names[^1];
                set => _names.Add(value);
            }

            // Indexer whose accessor scans rather than indexing.
            public int this[string name]
            {
                get
                {
                    for (var i = 0; i < _names.Count; i++)
                    {
                        if (_names[i] == name) return i;
                    }

                    return -1;
                }
            }

            public static Catalogue operator +(Catalogue a, Catalogue b)
            {
                foreach (var name in b._names) a._names.Add(name);
                return a;
            }

            public int Count() => _names.Count;

            // Same name as the BCL Count property, but this getter
            // scans. The name must not make the read free.
            public int Count
            {
                get
                {
                    var total = 0;
                    foreach (var value in _values) total += value;
                    return total;
                }
            }
        }
        """;

    private readonly ITestOutputHelper _output;

    public MemberSurfaceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void ExpensiveAccessorsAndOperators_AreReported()
    {
        var names = Analyze()
            .Select(f => f.Name)
            .ToArray();

        _output.WriteLine(string.Join(", ", names));

        Assert.Contains("Total.get", names);
        Assert.Contains("Largest.get", names);
        Assert.Contains("Newest.set", names);
        Assert.Contains("this[].get", names);
        Assert.Contains("Count.get", names);
        Assert.Contains("op_Addition", names);
    }

    [Fact]
    public void AutoProperties_AreNotReported()
    {
        var names = Analyze().Select(f => f.Name).ToArray();

        Assert.DoesNotContain("Title.get", names);
        Assert.DoesNotContain("Title.set", names);
    }

    [Theory]
    [InlineData("Total.get", "O(n)")]
    [InlineData("Largest.get", "O(n)")]
    [InlineData("this[].get", "O(n)")]
    [InlineData("Count.get", "O(n)")]
    public void ScanningAccessor_ReportsItsRealBound(
        string name, string time)
    {
        var fn = Analyze().Single(f => f.Name == name);
        _output.WriteLine(
            $"{name}: {ComplexityFormatter.FormatBigO(fn.Result.Time)}");
        Assert.Equal(
            time, ComplexityFormatter.FormatBigO(fn.Result.Time));
    }

    [Fact]
    public void Accessor_CarriesThePropertyKind()
    {
        var fn = Analyze().Single(f => f.Name == "Largest.get");
        Assert.Equal(
            Microsoft.CodeAnalysis.MethodKind.PropertyGet,
            fn.Symbol.MethodKind);
    }

    private static IEnumerable<(string Name, ComplexityResult Result,
        Microsoft.CodeAnalysis.IMethodSymbol Symbol)> Analyze()
    {
        var analysis = new CSharpFileAnalyzer()
            .Analyze(Source, AnalysisTier.Fast);
        return analysis.Functions.Select(f =>
            (DisplayName(f.Symbol), f.Result, f.Symbol));
    }

    /// <summary>
    /// Mirrors the server's display naming so the assertions read the
    /// way the panel does.
    /// </summary>
    private static string DisplayName(
        Microsoft.CodeAnalysis.IMethodSymbol symbol)
    {
        if (symbol.AssociatedSymbol is { } member)
        {
            var accessor = symbol.MethodKind switch
            {
                Microsoft.CodeAnalysis.MethodKind.PropertyGet => ".get",
                Microsoft.CodeAnalysis.MethodKind.PropertySet => ".set",
                _ => string.Empty,
            };
            return member.Name + accessor;
        }

        return symbol.Name;
    }
}
