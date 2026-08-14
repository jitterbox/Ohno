using ComplexityAnalyzer.Core;

namespace ComplexityAnalyzer.Tests;

public class EvidencePrunerTests
{
    [Fact]
    public void DropsEmptyLeavesAndCollapsesSingleChildSequence()
    {
        var enqueue = ComplexityEvidence.Leaf(
            "call", "Enqueue", Cx.Log(Cx.Var("k")));
        var empty = ComplexityEvidence.Leaf(
            "sequence", "empty", Cx.One);
        var wrapped = new ComplexityEvidence(
            "sequence",
            "sequential statements",
            Cx.Log(Cx.Var("k")),
            null,
            new[] { empty, empty, enqueue });

        var pruned = EvidencePruner.Prune(wrapped);

        Assert.Equal("Enqueue", pruned.Label);
        Assert.Empty(pruned.Children);
    }

    [Fact]
    public void SequentialCompositionOmitsEmptyChildren()
    {
        var empty = ComposedCost.Unit("sequence", "empty", null);
        var call = ComposedCost.Of(
            Cx.Log(Cx.Var("k")),
            Cx.One,
            "call",
            "Enqueue",
            null);
        var composed = CostComposer.Sequential(
            new[] { empty, empty, call }, null);

        Assert.Equal("Enqueue", composed.Evidence.Label);
        Assert.DoesNotContain(
            composed.Evidence.Children,
            c => c.Label == "empty");
    }
}
