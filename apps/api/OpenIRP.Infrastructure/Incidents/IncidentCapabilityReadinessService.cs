using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<ExecutionCapabilityReadiness> CapabilityReadinessAsync(Guid actor, Guid organization, Guid incident,
        Guid execution, Guid? task = null, CancellationToken ct = default) =>
        ExecutionRead(actor, organization, incident, async () =>
        {
            var run = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution &&
                e.OrganizationId == organization && e.IncidentId == incident, ct) ?? throw Error(404, "execution_not_found");
            var parent = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident && i.OrganizationId == organization, ct);
            var version = await db.PlaybookVersions.AsNoTracking().SingleAsync(v => v.Id == run.VersionId && v.PlaybookId == run.PlaybookId, ct);
            var tasks = await db.ExecutionTasks.AsNoTracking().Where(t => t.ExecutionId == execution &&
                t.OrganizationId == organization && t.IncidentId == incident).Take(101).ToListAsync(ct);
            if (tasks.Count is < 1 or > 100) throw Error(409, "invalid_execution_tasks");
            var selected = task is Guid id ? tasks.SingleOrDefault(t => t.Id == id) ?? throw Error(404, "execution_task_not_found") : null;
            var query = db.PlaybookCapabilityRequirements.AsNoTracking().Where(r => r.VersionId == run.VersionId);
            if (selected is not null) query = query.Where(r => r.StepId == null || r.StepId == selected.StepId);
            var requirements = await query.Take(301).ToListAsync(ct);
            if (requirements.Count > 300) throw Error(409, "invalid_execution_requirements");
            var ids = requirements.Select(r => r.CapabilityId).Distinct().ToArray();
            // One batch resolves every referenced capability, within ExecutionRead's RepeatableRead snapshot.
            var rows = await (from catalog in db.CapabilityCatalog.AsNoTracking()
                              join profile in db.OrganizationCapabilities.AsNoTracking().Where(p => p.OrganizationId == organization)
                                  on catalog.Id equals profile.CapabilityId into profiles
                              from profile in profiles.DefaultIfEmpty()
                              join assessment in db.IncidentCapabilityAssessments.AsNoTracking().Where(a => a.OrganizationId == organization && a.IncidentId == incident)
                                  on catalog.Id equals assessment.CapabilityId into assessments
                              from assessment in assessments.DefaultIfEmpty()
                              where ids.Contains(catalog.Id)
                              select new { Catalog = catalog, Profile = profile, Assessment = assessment }).ToListAsync(ct);
            var capabilities = rows.ToDictionary(r => r.Catalog.Id, r => CapabilityView(r.Catalog, r.Profile, r.Assessment));
            var steps = await db.PlaybookSteps.AsNoTracking().Where(s => s.VersionId == run.VersionId).ToDictionaryAsync(s => s.Id, ct);
            var byStep = tasks.ToDictionary(t => t.StepId);
            var result = new List<ExecutionCapabilityRequirement>();
            foreach (var requirement in requirements)
            {
                if (!capabilities.TryGetValue(requirement.CapabilityId, out var capability))
                    throw Error(409, "execution_capability_reference_missing");
                var step = requirement.StepId is Guid stepId ? steps[stepId] : null;
                var boundTask = step is not null ? byStep[step.Id] : null;
                result.Add(new(requirement.Id, step?.Id, boundTask?.Id, step?.Code, boundTask?.Status,
                    requirement.Level, CapabilityRequirementEvaluation.Evaluate(capability.IsActive, capability.EffectiveAvailability), capability));
            }
            var ordered = result.OrderBy(r => r.StepId is null ? 0 : 1)
                .ThenBy(r => r.StepId is Guid stepId ? steps[stepId].Position : 0)
                .ThenBy(r => r.Capability.Code, StringComparer.Ordinal).ThenBy(r => r.RequirementId).ToList();
            return new ExecutionCapabilityReadiness(run.Id, run.VersionId, version.Status, run.Status, run.Revision, parent.Revision,
                parent.Status, selected?.Id, selected?.Status,
                CapabilityRequirementEvaluation.Summarize(ordered.Where(r => r.Level == PlaybookRequirementLevel.Required).Select(r => r.Coverage)), ordered);
        }, ct);
}
