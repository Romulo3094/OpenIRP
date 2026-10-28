using System.Text.Json.Serialization;
namespace OpenIRP.Application.Assistance;

public sealed record AssistanceCapability(string Code,string Requirement,bool Active,string Availability);
public sealed record AssistanceDependency(string StepCode,string Status);
public sealed record AssistanceInput(string SchemaVersion,string Category,string? Severity,string? MaximumSeverity,
    string? Confidentiality,string? Integrity,string? Availability,string? Scope,string? OperationalImpact,string? Criticality,
    string StepCode,string Phase,string Instructions,string TaskStatus,bool IsRequired,bool RequiresApproval,bool RequiresEvidence,
    IReadOnlyList<AssistanceCapability> Capabilities,IReadOnlyList<AssistanceDependency> Predecessors);
public sealed record AssistanceContext(Guid TaskId,long IncidentRevision,long ExecutionRevision,string ContextToken,AssistanceInput Input);
public sealed record ExplainStep([property:JsonRequired] long ExpectedIncidentRevision,[property:JsonRequired] long ExpectedExecutionRevision,
    string ExpectedContext,string Reason);
public sealed record StepExplanation(string StepCode,string Guidance,IReadOnlyList<string> Cautions,IReadOnlyList<string> Assumptions);
public sealed record LocalAssistantReply(string Model,string? ModelDigest,string Outcome,string? ErrorCode,StepExplanation? Recommendation);
public interface ILocalAssistant
{
    Task<LocalAssistantReply> ExplainAsync(AssistanceInput input,CancellationToken ct=default);
}
public sealed record AssistanceAssessmentItem(Guid Id,Guid TaskId,long IncidentRevision,long ExecutionRevision,
    string ContextToken,string InputHash,string SchemaVersion,string PromptVersion,string Kind,string Model,string? ModelDigest,
    long DurationMs,string Outcome,string? ErrorCode,StepExplanation? Recommendation,Guid ActorUserId,Guid CorrelationId,
    DateTimeOffset OccurredAt,bool IsCurrent)
{
    public bool RequiresHumanReview => true;
    public bool AuthorizesExecution => false;
    public bool ManualOperationAvailable => true;
    public bool OriginalResponseStored => false;
}
public sealed record AssistanceAssessmentPage(IReadOnlyList<AssistanceAssessmentItem> Items,int? NextSkip);
public interface IAssistanceService
{
    Task<AssistanceContext> AssistanceContextAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,CancellationToken ct=default);
    Task<AssistanceAssessmentItem> ExplainStepAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,ExplainStep request,CancellationToken ct=default);
    Task<AssistanceAssessmentPage> AssistanceHistoryAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,int skip=0,int limit=50,CancellationToken ct=default);
}
