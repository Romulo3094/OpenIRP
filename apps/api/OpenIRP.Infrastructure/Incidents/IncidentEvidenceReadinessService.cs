using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<ExecutionEvidenceReadiness> EvidenceReadinessAsync(Guid actor, Guid organization, Guid incident,
        Guid execution, CancellationToken ct = default) => ExecutionRead(actor, organization, incident, async () =>
    {
        var run = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId == organization
            && e.IncidentId == incident && e.Id == execution, ct) ?? throw Error(404, "execution_not_found");
        var parent = await db.Incidents.AsNoTracking().SingleAsync(i => i.OrganizationId == organization && i.Id == incident, ct);
        var versionStatus = await db.PlaybookVersions.AsNoTracking().Where(v => v.Id == run.VersionId && v.PlaybookId == run.PlaybookId)
            .Select(v => v.Status).SingleOrDefaultAsync(ct) ?? throw Error(409, "execution_version_reference_missing");
        var admin = await IsEvidenceAdmin(actor, organization, ct);
        var tasks = db.ExecutionTasks.AsNoTracking().Where(t => t.OrganizationId == organization && t.IncidentId == incident && t.ExecutionId == execution);
        var count = await tasks.CountAsync(ct);
        if (count is < 1 or > 100) throw Error(409, "invalid_execution_tasks");
        var evidence = db.IncidentEvidence.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident
            && e.ExecutionId == execution && e.VersionId == run.VersionId)
            .Select(e => new { e.Id, e.TaskId, e.Kind, e.Revision, e.Status, e.Classification });
        var files = db.EvidenceFiles.AsNoTracking().Where(f => f.OrganizationId == organization && f.IncidentId == incident)
            .Select(f => new { f.EvidenceId, f.Revision, f.State });
        // One bounded batch joins every exact completion reference. Do not select content, title or storage metadata.
        var rows = await (from t in tasks.Where(t => t.VersionId == run.VersionId)
            join s in db.PlaybookSteps.AsNoTracking().Where(s => s.VersionId == run.VersionId) on t.StepId equals s.Id
            join e in evidence on new { Id = t.CompletionEvidenceId, Task = (Guid?)t.Id }
                equals new { Id = (Guid?)e.Id, Task = e.TaskId } into support
            from e in support.DefaultIfEmpty()
            join f in files on (Guid?)e.Id equals (Guid?)f.EvidenceId into metadata
            from f in metadata.DefaultIfEmpty()
            orderby s.Position, s.Code, t.Id
            select new { TaskId = t.Id, t.StepId, StepCode = s.Code, s.Position, TaskStatus = t.Status,
                s.IsRequired, s.RequiresEvidence, t.CompletionEvidenceId, EvidenceId = (Guid?)e.Id,
                EvidenceClassification = e.Classification, EvidenceKind = e.Kind, EvidenceRevision = (long?)e.Revision, EvidenceStatus = e.Status,
                FileRevision = (long?)f.Revision, FileState = f.State }).Take(101).ToListAsync(ct);
        if (rows.Count != count) throw Error(409, "invalid_execution_task_definition");
        var items = rows.Select(r =>
        {
            var hidden = !admin && r.EvidenceClassification == "RESTRICTED";
            return new TaskEvidenceSupport(r.TaskId, r.StepId, r.StepCode, r.Position, r.TaskStatus,
                r.IsRequired, r.RequiresEvidence, hidden ? null : r.CompletionEvidenceId,
                hidden ? null : r.EvidenceKind, hidden ? null : r.EvidenceRevision, hidden ? null : r.EvidenceStatus,
                !hidden && r.EvidenceKind == "FILE" ? r.FileRevision : null, !hidden && r.EvidenceKind == "FILE" ? r.FileState : null,
                hidden ? TaskEvidenceSupportState.AccessRestricted : EvidenceSupport(r.RequiresEvidence,r.TaskStatus,r.CompletionEvidenceId,r.EvidenceId,r.EvidenceKind,r.EvidenceStatus,r.FileState));
        }).ToList();
        return new ExecutionEvidenceReadiness(run.Id, run.VersionId, versionStatus, run.Status, run.Revision, parent.Revision,
            parent.Status, await RiskContext(organization, incident, ct), items);
    }, ct);

    private static TaskEvidenceSupportState EvidenceSupport(bool required, string taskStatus, Guid? reference,
        Guid? matched, string? kind, string? status, string? fileState)
    {
        if (reference is null)
        {
            if (!required) return TaskEvidenceSupportState.NotRequired;
            return taskStatus switch {
                "COMPLETED" => TaskEvidenceSupportState.MissingCompletionReference,
                "SKIPPED" => TaskEvidenceSupportState.SkippedNeedsReview,
                _ => TaskEvidenceSupportState.AwaitingCompletion
            };
        }
        if (taskStatus != "COMPLETED" || matched != reference || kind is not ("TEXT" or "REFERENCE" or "FILE"))
            return TaskEvidenceSupportState.InvalidCompletionReference;
        if (status == "WITHDRAWN") return TaskEvidenceSupportState.Withdrawn;
        if (status != "ACTIVE") return TaskEvidenceSupportState.InvalidCompletionReference;
        return kind == "FILE" && fileState != "AVAILABLE" ? TaskEvidenceSupportState.FileUnavailable : TaskEvidenceSupportState.MetadataAvailable;
    }
}
