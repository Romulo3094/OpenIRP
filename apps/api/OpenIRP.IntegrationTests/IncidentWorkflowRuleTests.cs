using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed class IncidentWorkflowRuleTests
{
    [Fact, Trait("Feature","IncidentWorkflow")]
    public void OnlyTheNinePlannedTransitionsArePermitted()
    {
        (IncidentState From, IncidentState To)[] allowed = [
            (IncidentState.Registered, IncidentState.UnderEvaluation),
            (IncidentState.UnderEvaluation, IncidentState.InResponse),
            (IncidentState.UnderEvaluation, IncidentState.Discarded),
            (IncidentState.InResponse, IncidentState.UnderEvaluation),
            (IncidentState.InResponse, IncidentState.Resolved),
            (IncidentState.Resolved, IncidentState.InResponse),
            (IncidentState.Resolved, IncidentState.Closed),
            (IncidentState.Closed, IncidentState.UnderEvaluation),
            (IncidentState.Discarded, IncidentState.UnderEvaluation)
        ];
        var count=0;
        foreach(var from in Enum.GetValues<IncidentState>())
        foreach(var to in Enum.GetValues<IncidentState>())
        {
            var actual=IncidentWorkflow.CanTransition(IncidentWorkflow.Store(from),to);
            Assert.Equal(allowed.Contains((from,to)),actual);
            if(actual)count++;
        }
        Assert.Equal(9,count);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public void UnknownStateValuesAreNotAccepted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>IncidentWorkflow.Store((IncidentState)999));
        Assert.False(IncidentWorkflow.CanTransition("UNKNOWN",IncidentState.UnderEvaluation));
        Assert.False(IncidentWorkflow.CanTransition("REGISTERED",(IncidentState)999));
    }
}
