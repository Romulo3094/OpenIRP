using OpenIRP.Domain.Playbooks;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed class PlaybookDependencyGraphTests
{
    [Fact, Trait("Feature", "PlaybookDrafts")]
    public void EmptyAndDisconnectedGraphsAreAcyclic()
    {
        Assert.True(PlaybookDependencyGraph.IsAcyclic([], []));
        Assert.True(PlaybookDependencyGraph.IsAcyclic(["A", "B", "C"], []));
    }
    [Fact, Trait("Feature", "PlaybookDrafts")]
    public void BranchesAndJoinAreValidRegardlessOfPresentationOrder()
    {
        Assert.True(PlaybookDependencyGraph.IsAcyclic(["D", "C", "B", "A"], [("A","B"),("A","C"),("B","D"),("C","D")]));
    }
    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData("self")]
    [InlineData("cycle")]
    [InlineData("disconnected_cycle")]
    [InlineData("unknown")]
    public void RejectsCyclesAndUnknownReferences(string kind)
    {
        var edges = kind switch
        {
            "self" => new[] { ("A","A") },
            "cycle" => [("A","B"),("B","A")],
            "disconnected_cycle" => [("B","C"),("C","B")],
            _ => [("A","X")]
        };
        Assert.False(PlaybookDependencyGraph.IsAcyclic(["A","B","C"], edges));
    }
}
