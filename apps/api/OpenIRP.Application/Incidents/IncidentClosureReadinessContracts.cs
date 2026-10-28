using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record ClosureStatusCount(string Status, int Count);
public sealed record IncidentClosureReadiness(Guid IncidentId, Guid OrganizationId, long IncidentRevision,
    string IncidentStatus, bool ClassificationComplete, bool HasResolution, bool AssignedWriterActive,
    bool AdministrativeApprovalRequired, IncidentClosureReview? ClosureReview, bool ClosureRequestCurrent,
    bool CanCloseDirectly, bool CanRequestClosure, bool CanApproveClosure, bool SelfApprovalDeclarationRequired,
    IReadOnlyList<string> Blockers, IReadOnlyList<ClosureStatusCount> Executions,
    IReadOnlyList<ClosureStatusCount> Tasks, IReadOnlyList<ClosureStatusCount> Evidence,
    int ActiveUnavailableFiles, int HistoricalDependencyApprovals, int DependencyRevocations,
    Guid? ActiveExecutionId, long? ActiveExecutionRevision, Guid? ActiveSecurityHoldId, bool ContinuityAuthorized, string ClosureContextToken, ClosureRiskState RiskReview)
{
    public bool RequiresHumanReview => true;
    public bool ResidualRiskEvaluated => RiskReview.IsCurrent;
    public bool FileIntegrityVerified => false;
}
