using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Executions;

public sealed record StartPlaybookExecution([property: JsonRequired] Guid PlaybookId,
    [property: JsonRequired] Guid VersionId, [property: JsonRequired] long ExpectedPlaybookRevision,
    [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record ExecutionCommand([property: JsonRequired] long ExpectedExecutionRevision,
    [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record AssignExecutionTask([property: JsonRequired] Guid? AssignedMembershipId,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record ChangeExecutionTask([property: JsonRequired] ExecutionTaskState TargetState,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason, Guid? CompletionEvidenceId = null);
public sealed record ExecutionTaskItem(Guid Id, Guid StepId, string Code, int Position, string Phase, string Instructions,
    bool IsRequired, bool RequiresApproval, bool RequiresEvidence, string Status, Guid? AssignedMembershipId, long ChangedAtRevision, Guid? CompletionEvidenceId = null);
public sealed record ExecutionDependencyItem(Guid PredecessorTaskId, Guid SuccessorTaskId);
public sealed record ExecutionRequirementItem(Guid CapabilityId, Guid? StepId, PlaybookRequirementLevel Level);
public sealed record ExecutionItem(Guid Id, Guid OrganizationId, Guid IncidentId, Guid PlaybookId, Guid VersionId,
    string VersionTitle, int VersionNumber, string VersionStatus, string Status, long Revision, long IncidentRevision,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string? FinishReason, IReadOnlyList<ExecutionTaskItem> Tasks,
    IReadOnlyList<ExecutionDependencyItem> Dependencies, IReadOnlyList<ExecutionRequirementItem> Requirements);
public sealed record ExecutionSummary(Guid Id, Guid VersionId, string Status, long Revision, DateTimeOffset CreatedAt);
public sealed record ExecutionPage(IReadOnlyList<ExecutionSummary> Items, int? NextSkip);
public sealed record ExecutionEventItem(Guid Id, long Revision, long IncidentRevision, Guid? TaskId, string Action,
    string ExecutionStatus, string? TaskStatus, Guid? AssignedMembershipId, Guid ActorUserId, Guid CorrelationId,
    string Reason, DateTimeOffset OccurredAt);
public interface IExecutionService
{
    Task<ExecutionItem> RequestApprovalAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, ExecutionCommand request, CancellationToken ct = default);
    Task<ExecutionItem> DecideApprovalAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, Guid approvalRequest, DecideStepApproval request, CancellationToken ct = default);
    Task<IReadOnlyList<StepApprovalItem>> ApprovalsAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, long afterRevision = 0, int limit = 50, CancellationToken ct = default);
    Task<ExecutionPage> ListAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<ExecutionItem> GetAsync(Guid actor, Guid organization, Guid incident, Guid execution, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionEventItem>> EventsAsync(Guid actor, Guid organization, Guid incident, Guid execution, long afterRevision = 0, int limit = 50, CancellationToken ct = default);
    Task<ExecutionItem> StartAsync(Guid actor, Guid organization, Guid incident, StartPlaybookExecution request, CancellationToken ct = default);
    Task<ExecutionItem> AssignAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, AssignExecutionTask request, CancellationToken ct = default);
    Task<ExecutionItem> ChangeAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, ChangeExecutionTask request, CancellationToken ct = default);
    Task<ExecutionItem> CompleteAsync(Guid actor, Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct = default);
    Task<ExecutionItem> CancelAsync(Guid actor, Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct = default);
}
