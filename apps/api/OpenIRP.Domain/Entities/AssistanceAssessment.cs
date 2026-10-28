namespace OpenIRP.Domain.Entities;

public sealed class AssistanceAssessment
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid OrganizationId {get;set;}
    public Guid IncidentId {get;set;}
    public Guid ExecutionId {get;set;}
    public Guid TaskId {get;set;}
    public Guid VersionId {get;set;}
    public long IncidentRevision {get;set;}
    public long ExecutionRevision {get;set;}
    public string ContextToken {get;set;}="";
    public string ContextJson {get;set;}="";
    public string InputHash {get;set;}="";
    public string SchemaVersion {get;set;}="";
    public string PromptVersion {get;set;}="";
    public string Kind {get;set;}="EXPLAIN_STEP";
    public string Model {get;set;}="";
    public string? ModelDigest {get;set;}
    public string ParametersJson {get;set;}="";
    public string Reason {get;set;}="";
    public long DurationMs {get;set;}
    public string Outcome {get;set;}="UNAVAILABLE";
    public string? ErrorCode {get;set;}
    public string? RecommendationJson {get;set;}
    public Guid ActorUserId {get;set;}
    public Guid CorrelationId {get;set;}
    public DateTimeOffset OccurredAt {get;set;}
}
