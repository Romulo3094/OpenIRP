using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Severity;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed class SeverityRuleV1Tests
{
    private static SeverityInputs Complete => new(ImpactLevel.None, ImpactLevel.None, ImpactLevel.None,
        IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.None);

    [Theory]
    [InlineData(ImpactLevel.None, IncidentCriticality.NotApplicable, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.Low)]
    [InlineData(ImpactLevel.Low, IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.Low)]
    [InlineData(ImpactLevel.Low, IncidentCriticality.Low, IncidentScope.Multiple, OperationalImpact.None, IncidentSeverity.Medium)]
    [InlineData(ImpactLevel.Medium, IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.Medium)]
    [InlineData(ImpactLevel.Medium, IncidentCriticality.High, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.High)]
    [InlineData(ImpactLevel.High, IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.High)]
    [InlineData(ImpactLevel.High, IncidentCriticality.High, IncidentScope.Single, OperationalImpact.None, IncidentSeverity.Critical)]
    [InlineData(ImpactLevel.High, IncidentCriticality.Low, IncidentScope.OrganizationWide, OperationalImpact.None, IncidentSeverity.Critical)]
    [InlineData(ImpactLevel.None, IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.MajorInterruption, IncidentSeverity.High)]
    [InlineData(ImpactLevel.None, IncidentCriticality.Low, IncidentScope.Single, OperationalImpact.CriticalInterruption, IncidentSeverity.Critical)]
    public void DocumentedCompleteScenarios(ImpactLevel cia, IncidentCriticality criticality, IncidentScope scope, OperationalImpact operation, IncidentSeverity expected)
    {
        var result = SeverityRuleV1.Evaluate(Complete with { Confidentiality = cia, Criticality = criticality, Scope = scope, OperationalImpact = operation });
        Assert.Equal(expected, result.Severity); Assert.Equal("Classified", result.ClassificationStatus);
        Assert.Equal(SeverityRuleV1.Version, result.RuleVersion);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void EachUnknownInputRequiresPendingEvenWithCriticalEvidence(int unknown)
    {
        var inputs = new SeverityInputs(ImpactLevel.High, ImpactLevel.High, ImpactLevel.High, IncidentCriticality.High, IncidentScope.OrganizationWide, OperationalImpact.CriticalInterruption);
        inputs = unknown switch {
            0 => inputs with { Confidentiality = null }, 1 => inputs with { Integrity = null },
            2 => inputs with { Availability = null }, 3 => inputs with { Criticality = null },
            4 => inputs with { Scope = null }, _ => inputs with { OperationalImpact = null }
        };
        var result = SeverityRuleV1.Evaluate(inputs);
        Assert.Null(result.Severity); Assert.Equal("Pending", result.ClassificationStatus);
        Assert.Equal("INCOMPLETE_INPUTS", result.MatchedRule);
    }

    [Fact]
    public void AllCompleteCombinationsAreClassifiedAndCiaOrderDoesNotMatter()
    {
        var count = 0;
        foreach (var input in Combinations())
        {
            var result = SeverityRuleV1.Evaluate(input);
            Assert.NotNull(result.Severity); Assert.True(Enum.IsDefined(result.Severity.Value));
            Assert.Equal(result.Severity, SeverityRuleV1.Evaluate(input with {
                Confidentiality = input.Integrity, Integrity = input.Availability, Availability = input.Confidentiality }).Severity);
            count++;
        }
        Assert.Equal(3072, count);
    }

    [Fact]
    public void IncreasingAnyOrderedInputCannotReduceSeverity()
    {
        var comparisons = 0;
        foreach (var input in Combinations())
        {
            var baseline = SeverityRuleV1.Evaluate(input).Severity!.Value;
            var neighbors = new List<SeverityInputs>();
            if (input.Confidentiality < ImpactLevel.High) neighbors.Add(input with { Confidentiality = input.Confidentiality + 1 });
            if (input.Integrity < ImpactLevel.High) neighbors.Add(input with { Integrity = input.Integrity + 1 });
            if (input.Availability < ImpactLevel.High) neighbors.Add(input with { Availability = input.Availability + 1 });
            if (input.Criticality is IncidentCriticality.Low or IncidentCriticality.Medium) neighbors.Add(input with { Criticality = input.Criticality + 1 });
            if (input.Scope < IncidentScope.OrganizationWide) neighbors.Add(input with { Scope = input.Scope + 1 });
            if (input.OperationalImpact < OperationalImpact.CriticalInterruption) neighbors.Add(input with { OperationalImpact = input.OperationalImpact + 1 });
            foreach (var neighbor in neighbors) { Assert.True(SeverityRuleV1.Evaluate(neighbor).Severity!.Value >= baseline); comparisons++; }
        }
        Assert.Equal(12800, comparisons);
    }

    [Fact]
    public void HistoricalMaximumNeverDecreasesAndPendingCannotEraseIt()
    {
        IncidentSeverity?[] levels = [null, IncidentSeverity.Low, IncidentSeverity.Medium, IncidentSeverity.High, IncidentSeverity.Critical];
        foreach (var previous in levels)
        foreach (var current in levels)
        {
            var maximum = SeverityRuleV1.Maximum(previous, current);
            if (previous.HasValue) Assert.True(maximum >= previous);
            if (current.HasValue) Assert.True(maximum >= current);
            if (current is null) Assert.Equal(previous, maximum);
            if (previous is null) Assert.Equal(current, maximum);
        }
    }

    [Fact]
    public void InvalidEnumsAreRejectedEvenWhenOtherInputsAreUnknown()
    {
        var unknown = new SeverityInputs(null, null, null, null, null, null);
        foreach (var invalid in new[] {
            unknown with { Confidentiality = (ImpactLevel)99 }, unknown with { Integrity = (ImpactLevel)99 },
            unknown with { Availability = (ImpactLevel)99 }, unknown with { Criticality = (IncidentCriticality)99 },
            unknown with { Scope = (IncidentScope)99 }, unknown with { OperationalImpact = (OperationalImpact)99 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => SeverityRuleV1.Evaluate(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityRuleV1.Maximum((IncidentSeverity)99, null));
    }

    private static IEnumerable<SeverityInputs> Combinations()
    {
        foreach (var c in Enum.GetValues<ImpactLevel>())
        foreach (var i in Enum.GetValues<ImpactLevel>())
        foreach (var a in Enum.GetValues<ImpactLevel>())
        foreach (var criticality in Enum.GetValues<IncidentCriticality>())
        foreach (var scope in Enum.GetValues<IncidentScope>())
        foreach (var operation in Enum.GetValues<OperationalImpact>())
            yield return new(c, i, a, criticality, scope, operation);
    }
}
