using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private sealed record ExecutionFixture(Guid Platform, Guid Admin, Guid Membership, Guid Org, IncidentItem Incident, PlaybookDraftItem Playbook);
    private async Task<T> Executions<T>(Func<IExecutionService, Task<T>> action)
    { using var scope = app.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<IExecutionService>()); }

    private async Task<ExecutionFixture> ExecutionSeed(bool privatePlaybook = false, bool controls = false, bool approvals = false, bool approvalEvidence = false)
    {
        var seed = await Seed(); var content = await DraftContent();
        if (!controls) content = content with { Steps = content.Steps.Select(s => s with { RequiresApproval = false, RequiresEvidence = false }).ToList() };
        if (approvals) content = content with { Steps = content.Steps.Select((s, i) => s with { RequiresApproval = i == 0, RequiresEvidence = i == 0 && approvalEvidence }).ToList() };
        Guid? sourceOrg = privatePlaybook ? seed.Org.Id : null; var author = privatePlaybook ? seed.Admin : seed.Platform;
        var source = await Playbooks(s => s.CreateAsync(author, sourceOrg, new(content)));
        source = await DecidePlaybook(author, sourceOrg, await SubmitPlaybook(author, sourceOrg, source));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        incident = await MoveWorkflow(seed.Admin, seed.Org.Id, incident, IncidentState.UnderEvaluation);
        incident = await MoveWorkflow(seed.Admin, seed.Org.Id, incident, IncidentState.InResponse);
        return new(seed.Platform, seed.Admin, seed.Membership, seed.Org.Id, incident, source);
    }
    private Task<ExecutionItem> BeginExecution(ExecutionFixture f) =>
        Executions(s => s.StartAsync(f.Admin, f.Org, f.Incident.Id, new(f.Playbook.Playbook.Id, f.Playbook.VersionId, f.Playbook.Playbook.Revision, f.Incident.Revision, "Iniciar respuesta guiada")));
    private Task<ExecutionItem> AssignTask(ExecutionFixture f, ExecutionItem current, Guid task, Guid? member = null) =>
        Executions(s => s.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(member ?? f.Membership, current.Revision, current.IncidentRevision, "Asignación justificada")));
    private Task<ExecutionItem> TaskState(ExecutionFixture f, ExecutionItem current, Guid task, ExecutionTaskState target, Guid? actor = null) =>
        Executions(s => s.ChangeAsync(actor ?? f.Admin, f.Org, f.Incident.Id, current.Id, task, new(target, current.Revision, current.IncidentRevision, "Avance documentado")));
    private async Task<ExecutionItem> FinishAllTasks(ExecutionFixture f, ExecutionItem current)
    {
        foreach (var task in current.Tasks.OrderBy(t => t.Position).ToArray())
        {
            current = await AssignTask(f, current, task.Id);
            current = await TaskState(f, current, task.Id, ExecutionTaskState.InProgress);
            current = await TaskState(f, current, task.Id, ExecutionTaskState.Completed);
        }
        return current;
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionCreatesExactlyPublishedTasksAndCorrelatedIncidentHistory(bool privatePlaybook)
    {
        var f = await ExecutionSeed(privatePlaybook); var current = await BeginExecution(f);
        Assert.Equal("IN_PROGRESS", current.Status); Assert.Equal(1, current.Revision); Assert.Equal(4, current.IncidentRevision);
        Assert.Equal(f.Playbook.VersionId, current.VersionId); Assert.Equal(3, current.Tasks.Count); Assert.Equal(2, current.Dependencies.Count);
        Assert.All(current.Tasks, t => { Assert.Equal("PENDING", t.Status); Assert.Null(t.AssignedMembershipId); Assert.Equal(1, t.ChangedAtRevision); });
        Assert.Equal(f.Playbook.Steps.Select(s => s.Id), current.Tasks.Select(t => t.StepId));
        Assert.Equal(f.Playbook.Steps.Select(s => s.Instructions), current.Tasks.Select(t => t.Instructions));
        Assert.Equal(2, current.Requirements.Count);
        var events = await Executions(s => s.EventsAsync(f.Admin, f.Org, f.Incident.Id, current.Id));
        var first = Assert.Single(events); Assert.Equal("EXECUTION_STARTED", first.Action);
        var incidentEvent = (await Incidents(s => s.EventsAsync(f.Admin, f.Org, f.Incident.Id))).Items.Last();
        Assert.Equal(first.CorrelationId, incidentEvent.CorrelationId); Assert.Equal(first.IncidentRevision, incidentEvent.Revision);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.execution_started"));
        Assert.Equal(first.CorrelationId, audit.CorrelationId); Assert.DoesNotContain(first.Reason, JsonSerializer.Serialize(audit));
        Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, current.Id))));
        Assert.Single((await Executions(s => s.ListAsync(f.Admin, f.Org, f.Incident.Id))).Items);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task DependenciesAssignmentBlockingAndCompletionRequireExplicitWorkWithoutAutoClosingIncident()
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        var first = current.Tasks[0].Id; var second = current.Tasks[1].Id;
        current = await AssignTask(f, current, second);
        await Rejected(409, () => TaskState(f, current, second, ExecutionTaskState.InProgress), "execution_dependencies_not_completed");
        current = await AssignTask(f, current, first);
        current = await TaskState(f, current, first, ExecutionTaskState.Blocked);
        current = await TaskState(f, current, first, ExecutionTaskState.Pending);
        current = await TaskState(f, current, first, ExecutionTaskState.InProgress);
        await Rejected(409, () => Executions(s => s.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, first, new(null, current.Revision, current.IncidentRevision, "Desasignar en curso"))));
        current = await TaskState(f, current, first, ExecutionTaskState.Completed);
        await Rejected(409, () => TaskState(f, current, first, ExecutionTaskState.Pending));
        await Rejected(409, () => Executions(s => s.CompleteAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Finalizar incompleta"))));
        current = await TaskState(f, current, second, ExecutionTaskState.InProgress);
        current = await TaskState(f, current, second, ExecutionTaskState.Completed);
        current = await AssignTask(f, current, current.Tasks[2].Id);
        current = await TaskState(f, current, current.Tasks[2].Id, ExecutionTaskState.InProgress);
        current = await TaskState(f, current, current.Tasks[2].Id, ExecutionTaskState.Completed);
        var done = await Executions(s => s.CompleteAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Ejecución terminada")));
        Assert.Equal("COMPLETED", done.Status); Assert.NotNull(done.FinishedAt);
        Assert.Equal("IN_RESPONSE", (await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id))).Status);
        await Rejected(409, () => Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, done.Id, new(done.Revision, done.IncidentRevision, "Cancelar finalizada"))));
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData("approval")]
    [InlineData("evidence")]
    [InlineData("skip")]
    public async Task PendingApprovalEvidenceAndSkipControlsCannotBeBypassed(string control)
    {
        var f = await ExecutionSeed(controls: true); var current = await BeginExecution(f);
        var task = current.Tasks[control == "approval" ? 1 : 0].Id;
        current = await AssignTask(f, current, task);
        if (control == "skip") { await Rejected(409, () => TaskState(f, current, task, ExecutionTaskState.Skipped), "task_skip_authorization_not_available"); return; }
        if (control == "approval")
        {
            // Source step 2 also depends on step 1; use a fresh reviewed version without that dependency.
            var body = ContentForTest(f.Playbook) with { Dependencies = [] };
            var newer = await Playbooks(s => s.NewVersionAsync(f.Platform, null, f.Playbook.Playbook.Id, new(f.Playbook.VersionId, "Revisar dependencia", 3)));
            newer = await Playbooks(s => s.UpdateAsync(f.Platform, null, newer.Playbook.Id, new(body, "Quitar enlace en nueva versión revisada", 4)));
            newer = await DecidePlaybook(f.Platform, null, await SubmitPlaybook(f.Platform, null, newer));
            current = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Cambiar versión")));
            f = f with { Incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)), Playbook = newer };
            current = await BeginExecution(f); task = current.Tasks[1].Id; current = await AssignTask(f, current, task);
            await Rejected(409, () => TaskState(f, current, task, ExecutionTaskState.InProgress), "task_approval_required");
        }
        else
        {
            current = await TaskState(f, current, task, ExecutionTaskState.InProgress);
            await Rejected(409, () => TaskState(f, current, task, ExecutionTaskState.Completed), "task_evidence_required");
        }
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task TenantRolesHaveExplicitExecutionPermissionsAndNoGlobalBypass(OrganizationRole role)
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        await Rejected(403, () => Executions(s => s.GetAsync(f.Platform, f.Org, f.Incident.Id, current.Id)));
        var membership = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, role)));
        Assert.Equal(current.Id, (await Executions(s => s.GetAsync(f.Platform, f.Org, f.Incident.Id, current.Id))).Id);
        if (role == OrganizationRole.Viewer)
        {
            await Rejected(403, () => Executions(s => s.CancelAsync(f.Platform, f.Org, f.Incident.Id, current.Id, new(1, 4, "Viewer no cancela"))));
            await Rejected(409, () => AssignTask(f, current, current.Tasks[0].Id, membership.Id));
        }
        else
        {
            current = await AssignTask(f, current, current.Tasks[0].Id, membership.Id);
            await Rejected(409, () => TaskState(f, current, current.Tasks[0].Id, ExecutionTaskState.InProgress));
            current = await TaskState(f, current, current.Tasks[0].Id, ExecutionTaskState.InProgress, f.Platform);
            Assert.Equal("IN_PROGRESS", current.Tasks[0].Status);
        }
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task ExecutionReadsAndWritesRevalidateCurrentAuthorization(string inactive)
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        await InScope(async (_, db) =>
        {
            if (inactive == "membership") (await db.OrganizationMemberships.SingleAsync()).Status = MembershipStatus.Suspended;
            else if (inactive == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else if (inactive == "account") (await db.Users.SingleAsync(u => u.Id == f.Admin)).IsActive = false;
            else (await db.Users.SingleAsync(u => u.Id == f.Admin)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, current.Id)));
        await Rejected(403, () => Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(1, 4, "No autorizado"))));
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task ActiveExecutionPreventsIncidentResolutionDiscardCategoryChangesAndBacktracking()
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        foreach (var state in new[] { IncidentState.Resolved, IncidentState.UnderEvaluation })
            await Rejected(409, () => Incidents(s => s.TransitionAsync(f.Admin, f.Org, f.Incident.Id, new(state,
                state == IncidentState.Resolved ? "Resolución prematura" : null, "Intentar salir de respuesta", current.IncidentRevision))), "incident_has_active_execution");
        var otherCategory = await InScope((_, db) => db.IncidentCategories.Where(c => c.Id != f.Incident.CategoryId).Select(c => c.Id).FirstAsync());
        await Rejected(409, () => Incidents(s => s.UpdateAsync(f.Admin, f.Org, f.Incident.Id, new(otherCategory, "Categoría nueva", null, "Cambiar", current.IncidentRevision))));
        current = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(1, 4, "Cancelar con justificación")));
        Assert.Equal("CANCELLED", current.Status);
        var incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id));
        Assert.Equal("RESOLVED", (await MoveWorkflow(f.Admin, f.Org, incident, IncidentState.Resolved, "Resolución manual posterior")).Status);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task NoncurrentPublishedVersionCanContinueButWithdrawalBlocksWorkAndNeedsAdminCancellation()
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Analyst)));
        current = await AssignTask(f, current, current.Tasks[0].Id, member.Id);
        var newer = await Playbooks(s => s.NewVersionAsync(f.Platform, null, f.Playbook.Playbook.Id, new(f.Playbook.VersionId, "Nueva", 3)));
        newer = await DecidePlaybook(f.Platform, null, await SubmitPlaybook(f.Platform, null, newer));
        current = await TaskState(f, current, current.Tasks[0].Id, ExecutionTaskState.InProgress, f.Platform);
        await Playbooks(s => s.WithdrawAsync(f.Platform, null, f.Playbook.Playbook.Id, f.Playbook.VersionId, new("Retirar histórica", newer.Playbook.Revision)));
        await Rejected(409, () => TaskState(f, current, current.Tasks[0].Id, ExecutionTaskState.Completed, f.Platform), "execution_version_withdrawn");
        await Rejected(409, () => AssignTask(f, current, current.Tasks[1].Id));
        await Rejected(403, () => Executions(s => s.CancelAsync(f.Platform, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Analyst no autoriza retirada"))));
        current = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Cancelación administrativa de retirada")));
        Assert.Equal("CANCELLED", current.Status); Assert.Equal("ARCHIVED", current.VersionStatus);
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData("start")]
    [InlineData("task")]
    public async Task ConcurrentExecutionMutationsCannotLoseIncidentOrExecutionRevisions(string operation)
    {
        var f = await ExecutionSeed(); ExecutionItem? current = operation == "task" ? await BeginExecution(f) : null;
        if (current is not null) current = await AssignTask(f, current, current.Tasks[0].Id);
        async Task<int> Attempt()
        {
            try
            {
                if (current is null) await BeginExecution(f);
                else await TaskState(f, current, current.Tasks[0].Id, ExecutionTaskState.InProgress);
                return 200;
            }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(Attempt, Attempt)).Order().ToArray());
        Assert.Equal(1, await InScope((_, db) => db.PlaybookExecutions.CountAsync()));
        Assert.Equal(current is null ? 1 : 3, await InScope((_, db) => db.ExecutionTaskEvents.CountAsync()));
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task ExecutionReadsAndCommandsAreScopedToBothOrganizationAndIncident()
    {
        var f = await ExecutionSeed(privatePlaybook: true); var current = await BeginExecution(f);
        var other = await RegisterIncident(f.Admin, f.Org);
        await Rejected(404, () => Executions(s => s.GetAsync(f.Admin, f.Org, other.Id, current.Id)));
        var beta = await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaActor = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var betaIncident = await RegisterIncident(betaActor, beta.Id);
        betaIncident = await MoveWorkflow(betaActor, beta.Id, betaIncident, IncidentState.UnderEvaluation);
        betaIncident = await MoveWorkflow(betaActor, beta.Id, betaIncident, IncidentState.InResponse);
        await Rejected(404, () => Executions(s => s.StartAsync(betaActor, beta.Id, betaIncident.Id,
            new(f.Playbook.Playbook.Id, f.Playbook.VersionId, 3, betaIncident.Revision, "No importa privado de otro tenant"))));
        await Rejected(403, () => Executions(s => s.GetAsync(f.Admin, beta.Id, betaIncident.Id, current.Id)));
        await Rejected(404, () => Executions(s => s.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, Guid.NewGuid(), new(f.Membership, current.Revision, current.IncidentRevision, "Tarea ajena"))));
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task ExecutionValidatesSourceCategoryRevisionIncidentStateAndPaging()
    {
        var f = await ExecutionSeed();
        await Rejected(409, () => Executions(s => s.StartAsync(f.Admin, f.Org, f.Incident.Id,
            new(f.Playbook.Playbook.Id, f.Playbook.VersionId, 1, f.Incident.Revision, "Fuente obsoleta"))));
        var registered = await RegisterIncident(f.Admin, f.Org);
        await Rejected(409, () => Executions(s => s.StartAsync(f.Admin, f.Org, registered.Id, new(f.Playbook.Playbook.Id, f.Playbook.VersionId, 3, registered.Revision, "No está en respuesta"))));
        var current = await BeginExecution(f);
        await Rejected(409, () => Executions(s => s.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, current.Tasks[0].Id, new(f.Membership, 0, current.IncidentRevision, "Exec obsoleta"))));
        await Rejected(409, () => Executions(s => s.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, current.Tasks[0].Id, new(f.Membership, 1, 1, "Incidente obsoleto"))));
        await Rejected(400, () => Executions(s => s.EventsAsync(f.Admin, f.Org, f.Incident.Id, current.Id, -1)));
        await Rejected(400, () => Executions(s => s.ListAsync(f.Admin, f.Org, f.Incident.Id, 0, 101)));
    }
    [Theory, Trait("Feature", "Executions")]
    [InlineData("category")]
    [InlineData("capability")]
    [InlineData("withdrawn")]
    public async Task ExecutionStartRechecksCategoryCatalogAndCurrentPublication(string invalid)
    {
        var f = await ExecutionSeed();
        if (invalid == "category")
        {
            var category = await InScope((_, db) => db.IncidentCategories.Where(c => c.Id != f.Incident.CategoryId).Select(c => c.Id).FirstAsync());
            var changed = await Incidents(s => s.UpdateAsync(f.Admin, f.Org, f.Incident.Id,
                new(category, f.Incident.Title, f.Incident.Description, "Cambio anterior al inicio", f.Incident.Revision)));
            f = f with { Incident = changed };
        }
        else if (invalid == "capability")
            await InScope(async (_, db) => { (await db.CapabilityCatalog.SingleAsync(c => c.Id == f.Playbook.Requirements[0].CapabilityId)).IsActive = false; return await db.SaveChangesAsync(); });
        else
        {
            var withdrawn = await Playbooks(s => s.WithdrawAsync(f.Platform, null, f.Playbook.Playbook.Id, f.Playbook.VersionId, new("Retirar antes del inicio", 3)));
            f = f with { Playbook = withdrawn };
        }
        await Rejected(409, () => BeginExecution(f));
        Assert.Equal(0, await InScope((_, db) => db.PlaybookExecutions.CountAsync()));
    }

}
