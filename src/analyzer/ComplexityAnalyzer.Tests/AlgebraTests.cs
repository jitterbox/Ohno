using ComplexityAnalyzer.Core;
using Xunit;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Symbolic simplification tests, independent of Roslyn.
/// Expected values are formatted for readability; the assertions run against
/// the simplified symbolic expression.
/// </summary>
public class AlgebraTests
{
    private static string Simplify(ComplexityExpression expression) =>
        ComplexityFormatter.Format(ComplexitySimplifier.Simplify(expression));

    [Fact]
    public void ConstantPlusVariable_DropsConstant()
    {
        Assert.Equal("n", Simplify(Cx.Add(Cx.Var("n"), Cx.One)));
    }

    [Fact]
    public void SameVariableTwice_Collapses()
    {
        Assert.Equal("n", Simplify(Cx.Add(Cx.Var("n"), Cx.Var("n"))));
    }

    [Fact]
    public void QuadraticDominatesLinear()
    {
        var nSquared = Cx.Pow(Cx.Var("n"), Cx.Constant(2));
        Assert.Equal("n²", Simplify(Cx.Add(Cx.Var("n"), nSquared)));
    }

    [Fact]
    public void LinearithmicDominatesLinear()
    {
        var nLogN = Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("n")));
        Assert.Equal("n log n", Simplify(Cx.Add(Cx.Var("n"), nLogN)));
    }

    [Fact]
    public void IndependentDimensions_ArePreserved_InSum()
    {
        Assert.Equal("m + n", Simplify(Cx.Add(Cx.Var("n"), Cx.Var("m"))));
    }

    [Fact]
    public void IndependentDimensions_ArePreserved_InProduct()
    {
        Assert.Equal("m n", Simplify(Cx.Mul(Cx.Var("n"), Cx.Var("m"))));
    }

    [Fact]
    public void NodeWalkDominatesSeededHeap()
    {
        var nLogK = Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("k")));
        var kLogK = Cx.Mul(Cx.Var("k"), Cx.Log(Cx.Var("k")));
        Assert.Equal("n log k", Simplify(Cx.Add(nLogK, kLogK)));
    }

    [Fact]
    public void VerticesPlusEdges_TimesLog_Distributes()
    {
        var sum = Cx.Add(Cx.Var("n"), Cx.Var("m"));
        var product = Cx.Mul(sum, Cx.Log(Cx.Var("n")));
        Assert.Equal("m log n + n log n", Simplify(product));
    }

    [Fact]
    public void ProductDistributesAndSimplifies()
    {
        // n * (1 + log k) => n + n log k => n log k
        var expression = Cx.Mul(Cx.Var("n"), Cx.Add(Cx.One, Cx.Log(Cx.Var("k"))));
        Assert.Equal("n log k", Simplify(expression));
    }

    [Fact]
    public void ProductOfIndependentDimensions_DominatesEach()
    {
        // n + n*m => n*m
        var nm = Cx.Mul(Cx.Var("n"), Cx.Var("m"));
        Assert.Equal("m n", Simplify(Cx.Add(Cx.Var("n"), nm)));
    }

    [Fact]
    public void CrossVariablePowers_AreIncomparable()
    {
        // n² + n*m: neither dominates when n and m are independent.
        var nSquared = Cx.Pow(Cx.Var("n"), Cx.Constant(2));
        var nm = Cx.Mul(Cx.Var("n"), Cx.Var("m"));
        Assert.Equal("m n + n²", Simplify(Cx.Add(nSquared, nm)));
    }

    [Fact]
    public void ExponentialDominatesPolynomial()
    {
        var twoToN = Cx.Pow(Cx.Constant(2), Cx.Var("n"));
        var nSquared = Cx.Pow(Cx.Var("n"), Cx.Constant(2));
        Assert.Equal("2^n", Simplify(Cx.Add(twoToN, nSquared)));
    }

    [Fact]
    public void FactorialDominatesExponential()
    {
        var twoToN = Cx.Pow(Cx.Constant(2), Cx.Var("n"));
        var nFactorial = Cx.Factorial(Cx.Var("n"));
        Assert.Equal("n!", Simplify(Cx.Add(twoToN, nFactorial)));
    }

    [Fact]
    public void UnknownCall_RemainsVisible()
    {
        // n * C(Process) must not simplify to n.
        var expression = Cx.Mul(Cx.Var("n"), Cx.Call("Process"));
        Assert.Equal("n C(Process)", Simplify(expression));
    }

    [Fact]
    public void UnknownCall_PlusLinear_StaysSeparate()
    {
        var expression = Cx.Add(Cx.Var("n"), Cx.Call("Process"));
        Assert.Equal("C(Process) + n", Simplify(expression));
    }

    [Fact]
    public void CallScaledByDimension_DominatesBareCall()
    {
        // C(f) + n*C(f) => n*C(f)
        var scaled = Cx.Mul(Cx.Var("n"), Cx.Call("f"));
        Assert.Equal("n C(f)", Simplify(Cx.Add(Cx.Call("f"), scaled)));
    }

    [Fact]
    public void LogOfPower_LosesExponent()
    {
        // log(n²) => log n
        var logOfSquare = Cx.Log(Cx.Pow(Cx.Var("n"), Cx.Constant(2)));
        Assert.Equal("log n", Simplify(logOfSquare));
    }

    [Fact]
    public void SameBasePowers_Combine()
    {
        // n * n => n²
        Assert.Equal("n²", Simplify(Cx.Mul(Cx.Var("n"), Cx.Var("n"))));
    }

    [Fact]
    public void ConstantFactors_Drop()
    {
        Assert.Equal("n", Simplify(Cx.Mul(Cx.Constant(2), Cx.Var("n"))));
    }

    [Fact]
    public void Simplification_IsDeterministic()
    {
        ComplexityExpression Build() => Cx.Add(
            Cx.Mul(Cx.Var("n"), Cx.Log(Cx.Var("k"))),
            Cx.Var("m"),
            Cx.Mul(Cx.Var("n"), Cx.Var("m")));

        Assert.Equal(Simplify(Build()), Simplify(Build()));
    }
}
