using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed class CapabilityRequirementEvaluationTests
{
    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData(false,CapabilityAvailability.Available,CapabilityRequirementCoverage.InactiveCatalog)]
    [InlineData(false,CapabilityAvailability.Unavailable,CapabilityRequirementCoverage.InactiveCatalog)]
    [InlineData(false,CapabilityAvailability.Unknown,CapabilityRequirementCoverage.InactiveCatalog)]
    [InlineData(true,CapabilityAvailability.Available,CapabilityRequirementCoverage.DeclaredAvailable)]
    [InlineData(true,CapabilityAvailability.Unavailable,CapabilityRequirementCoverage.Unavailable)]
    [InlineData(true,CapabilityAvailability.Unknown,CapabilityRequirementCoverage.Unknown)]
    public void CatalogInactivityNeverProducesDeclaredAvailableCoverage(bool active,CapabilityAvailability availability,CapabilityRequirementCoverage expected) =>
        Assert.Equal(expected,CapabilityRequirementEvaluation.Evaluate(active,availability));

    [Fact, Trait("Feature","CapabilityReadiness")]
    public void KnownGapsRemainVisibleAlongsideUnknownsAndAvailableDeclarations()
    {
        Assert.Equal(RequiredCapabilityCoverage.NoRequiredCapabilities,CapabilityRequirementEvaluation.Summarize([]));
        Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,CapabilityRequirementEvaluation.Summarize([CapabilityRequirementCoverage.DeclaredAvailable]));
        Assert.Equal(RequiredCapabilityCoverage.Unknown,CapabilityRequirementEvaluation.Summarize([CapabilityRequirementCoverage.DeclaredAvailable,CapabilityRequirementCoverage.Unknown]));
        foreach(var gap in new[]{CapabilityRequirementCoverage.Unavailable,CapabilityRequirementCoverage.InactiveCatalog})
            Assert.Equal(RequiredCapabilityCoverage.Gaps,CapabilityRequirementEvaluation.Summarize([CapabilityRequirementCoverage.Unknown,gap,CapabilityRequirementCoverage.DeclaredAvailable]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CapabilityRequirementEvaluation.Evaluate(true,(CapabilityAvailability)999));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CapabilityRequirementEvaluation.Summarize([(CapabilityRequirementCoverage)999]));
    }
}
