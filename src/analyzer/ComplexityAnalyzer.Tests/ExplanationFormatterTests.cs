using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.Tests;

public class ExplanationFormatterTests
{
    [Theory]
    [InlineData("1", "Constant time")]
    [InlineData("n", "Linear time")]
    [InlineData("n²", "Quadratic time")]
    [InlineData("n log n", "Linearithmic time")]
    [InlineData("2^n", "Exponential time")]
    [InlineData("n!", "Factorial time")]
    public void Phrase_ForCommonBounds(string formatted, string phrase)
    {
        var time = formatted switch
        {
            "1" => Cx.One,
            "n" => Cx.Var("n"),
            "n²" => Cx.Pow(Cx.Var("n"), Cx.Constant(2)),
            "2^n" => Cx.Pow(Cx.Constant(2), Cx.Var("n")),
            "n!" => Cx.Factorial(Cx.Var("n")),
            _ => Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("n"))),
        };
        Assert.Equal(phrase, ExplanationFormatter.Format(time, []));
    }

    [Fact]
    public void Unknown_IncludesReason()
    {
        var patterns = new[]
        {
            new RecognizedPattern(
                "dynamic-dispatch",
                "Dynamic dispatch",
                "the invocation target is selected by the runtime binder",
                PatternEffect.Unknown),
        };
        var time = Cx.Unknown(
            "the invocation target is selected by the runtime binder");
        var text = ExplanationFormatter.Format(time, patterns);
        Assert.StartsWith("Unknown: The complexity cannot be easily", text);
        Assert.Contains("runtime binder", text);
    }

    [Fact]
    public void Range_UsesStatedExplanation()
    {
        var patterns = new[]
        {
            new RecognizedPattern(
                "cache-history",
                "Cache-dependent work",
                "a hit is constant time",
                PatternEffect.Range,
                "Worst case linear; a cache hit is constant time"),
        };
        Assert.Equal(
            "Worst case linear; a cache hit is constant time",
            ExplanationFormatter.Format(Cx.Var("n"), patterns));
    }

    [Fact]
    public void Empty_WhenNoHonestPhrase()
    {
        Assert.Equal("", ExplanationFormatter.Format(Cx.Call("Foo"), []));
    }
}
