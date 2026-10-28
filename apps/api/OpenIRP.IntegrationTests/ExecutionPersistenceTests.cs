using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    [Theory, Trait("Feature", "Executions")]
    [InlineData("start", "audit")]
    [InlineData("start", "task_events")]
    [InlineData("task", "incident_events")]
    [InlineData("task", "audit")]
    [InlineData("cancel", "task_events")]
    [InlineData("complete", "audit")]
    public async Task ExecutionFailuresRollBackBothAggregatesAllTasksAndBothHistories(string operation, string sink)
    {
        var f = await ExecutionSeed(); ExecutionItem? current = null;
        if (operation != "start") current = await BeginExecution(f);
        if (operation == "task") current = await AssignTask(f, current!, current!.Tasks[0].Id);
        if (operation == "complete") current = await FinishAllTasks(f, current!);
        var incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id));
        var incidentEvents = await InScope((_, db) => db.IncidentEvents.CountAsync());
        var taskEvents = await InScope((_, db) => db.ExecutionTaskEvents.CountAsync());
        var table = sink switch { "audit" => "audit.audit_events", "incident_events" => "app.incident_events", _ => "app.execution_task_events" };
        var script = "CREATE FUNCTION app.reject_execution_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected execution failure'; END; $body$; CREATE TRIGGER reject_execution_test BEFORE INSERT ON " + table
            + " FOR EACH ROW " + (sink == "audit" ? "WHEN (NEW.outcome='SUCCESS') " : "") + "EXECUTE FUNCTION app.reject_execution_test();";
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(script));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (operation == "start") await BeginExecution(f);
            else if (operation == "task") await TaskState(f, current!, current!.Tasks[0].Id, ExecutionTaskState.InProgress);
            else if (operation == "cancel") await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, current!.Id, new(current.Revision, current.IncidentRevision, "Cancelar")));
            else await Executions(s => s.CompleteAsync(f.Admin, f.Org, f.Incident.Id, current!.Id, new(current.Revision, current.IncidentRevision, "Finalizar")));
        });
        Assert.Equal(incident, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        Assert.Equal(incidentEvents, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(taskEvents, await InScope((_, db) => db.ExecutionTaskEvents.CountAsync()));
        if (current is null)
        {
            Assert.Equal(0, await InScope((_, db) => db.PlaybookExecutions.CountAsync()));
            Assert.Equal(0, await InScope((_, db) => db.ExecutionTasks.CountAsync()));
        }
        else Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, current.Id))));
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData("incident_state")]
    [InlineData("task_dependency")]
    [InlineData("execution_completion")]
    [InlineData("task_identity")]
    public async Task DatabaseGuardsPreventIllegalExecutionMutations(string invalid)
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        var query = invalid switch
        {
            "incident_state" => "UPDATE app.incidents SET status='RESOLVED',resolution='No debe cerrar',revision=revision+1",
            "execution_completion" => "UPDATE app.playbook_executions SET status='COMPLETED',finished_at=now(),finish_reason='Prematuro',revision=revision+1",
            "task_identity" => "UPDATE app.execution_tasks SET step_id=gen_random_uuid(),changed_at_revision=changed_at_revision+1",
            _ => $"UPDATE app.execution_tasks SET status='IN_PROGRESS',assigned_membership_id='{f.Membership}',changed_at_revision=changed_at_revision+1 WHERE id='{current.Tasks[1].Id}'"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(query)));
        Assert.Equal("23514", error.SqlState);
    }

    [Theory, Trait("Feature", "Executions")]
    [InlineData("events_update")]
    [InlineData("events_delete")]
    [InlineData("execution_delete")]
    [InlineData("task_delete")]
    public async Task DatabasePreservesExecutionHistoryAndTaskIdentity(string operation)
    {
        var f = await ExecutionSeed(); await BeginExecution(f);
        var sql = operation switch
        {
            "events_update" => "UPDATE app.execution_task_events SET reason='Alterado'",
            "events_delete" => "DELETE FROM app.execution_task_events",
            "execution_delete" => "DELETE FROM app.playbook_executions",
            _ => "DELETE FROM app.execution_tasks"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("42501", error.SqlState);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task DatabaseRequiresCompleteInitialTaskSetAndPreventsDuplicateActiveExecution()
    {
        var f = await ExecutionSeed();
        var empty = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookExecutions.Add(new() { OrganizationId = f.Org, IncidentId = f.Incident.Id,
                PlaybookId = f.Playbook.Playbook.Id, VersionId = f.Playbook.VersionId, CreatedByUserId = f.Admin });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(empty.InnerException).SqlState);
        await BeginExecution(f);
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookExecutions.Add(new() { OrganizationId = f.Org, IncidentId = f.Incident.Id,
                PlaybookId = f.Playbook.Playbook.Id, VersionId = f.Playbook.VersionId, CreatedByUserId = f.Admin });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23505", Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task TaskAndEventReferencesCannotMixVersionsOrOrganizations()
    {
        var f = await ExecutionSeed(); var current = await BeginExecution(f);
        var other = await PublishedPlaybook(f.Platform, null);
        var badTask = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.ExecutionTasks.Add(new() { OrganizationId = f.Org, IncidentId = f.Incident.Id,
                ExecutionId = current.Id, VersionId = current.VersionId, StepId = other.Steps[0].Id });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(badTask.InnerException).SqlState);
        var badEvent = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.ExecutionTaskEvents.Add(new() { OrganizationId = Guid.NewGuid(), IncidentId = f.Incident.Id,
                ExecutionId = current.Id, VersionId = current.VersionId, Revision = 2, IncidentRevision = current.IncidentRevision,
                ExecutionStatus = "IN_PROGRESS", Action = "EXECUTION_STARTED", Reason = "Cruce", ActorUserId = f.Admin, CorrelationId = Guid.NewGuid() });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(badEvent.InnerException).SqlState);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task ExecutionMigrationPreservesExistingIncidentAndBlocksLossyRollback()
    {
        var f = await ExecutionSeed();
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261024021251_PlaybookCloning"); await db.Database.MigrateAsync(); return true; });
        Assert.Equal(f.Incident, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        var current = await BeginExecution(f);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) =>
            { await db.GetService<IMigrator>().MigrateAsync("20261024021251_PlaybookCloning"); return true; }));
        Assert.Contains("Cannot roll back executions", error.MessageText);
        await InScope(async (_, db) => { await db.Database.MigrateAsync(); return true; });
        Assert.Equal(current.Id, (await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, current.Id))).Id);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task CompletedExecutionAllowsLaterClassifiedManualIncidentClosure()
    {
        var f = await ExecutionSeed(); var incident = await CompleteSeverityInputs(f.Admin, f.Org, f.Incident, ImpactLevel.None, IncidentCriticality.Low);
        incident = await Incidents(s => s.AssignAsync(f.Admin, f.Org, incident.Id, new(f.Membership, "Responsable", incident.Revision)));
        f = f with { Incident = incident };
        var current = await FinishAllTasks(f, await BeginExecution(f));
        current = await Executions(s => s.CompleteAsync(f.Admin, f.Org, incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Trabajo completado")));
        incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, incident.Id));
        Assert.Equal("IN_RESPONSE", incident.Status);
        incident = await MoveWorkflow(f.Admin, f.Org, incident, IncidentState.Resolved, "Resolución confirmada");
        incident = await AcceptRiskForTest(f.Admin, f.Org, incident);
        incident = await MoveWorkflow(f.Admin, f.Org, incident, IncidentState.Closed);
        Assert.Equal("CLOSED", incident.Status);
        Assert.Equal("COMPLETED", (await Executions(s => s.GetAsync(f.Admin, f.Org, incident.Id, current.Id))).Status);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task HttpExecutionRequiresCsrfAndExplicitRevisionAndAssigneeContracts()
    {
        var f = await ExecutionSeed(); var root = $"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions";
        using var anon = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(root)).StatusCode);
        using var admin = Client(); await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(root, new
        { playbookId = f.Playbook.Playbook.Id, versionId = f.Playbook.VersionId, reason = "Faltan tokens" })).StatusCode);
        var response = await admin.PostAsJsonAsync(root, new StartPlaybookExecution(f.Playbook.Playbook.Id, f.Playbook.VersionId, 3, 3, "Inicio HTTP"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var current = (await response.Content.ReadFromJsonAsync<ExecutionItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } }))!;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(response.Headers.Location)).StatusCode);
        var assignment = root + $"/{current.Id}/tasks/{current.Tasks[0].Id}/assignment";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(assignment, new
        { expectedExecutionRevision = 1, expectedIncidentRevision = 4, reason = "Sin destino explícito" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(assignment, new AssignExecutionTask(f.Membership, 1, 4, "Asignar HTTP"))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(root + $"/{current.Id}/cancellation", new ExecutionCommand(2, 5, "Sin CSRF"))).StatusCode);
    }

    [Fact, Trait("Feature", "Executions")]
    public async Task RestrictedRuntimeCanExecuteTasksAndAppendHistoriesWithoutEditingTheirIdentity()
    {
        var f = await ExecutionSeed(); var role = "openirp_execution_runtime_" + Guid.NewGuid().ToString("N");
        var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role', @role, false)", owner))
            { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using (var runtime = new TestApplication(builder.ConnectionString))
            {
                using var scope = runtime.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IExecutionService>();
                var current = await service.StartAsync(f.Admin, f.Org, f.Incident.Id, new(f.Playbook.Playbook.Id, f.Playbook.VersionId, 3, 3, "Runtime start"));
                var task = current.Tasks[0].Id;
                current = await service.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(f.Membership, 1, 4, "Runtime assign"));
                current = await service.ChangeAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(ExecutionTaskState.InProgress, 2, 5, "Runtime start task"));
                current = await service.ChangeAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(ExecutionTaskState.Completed, 3, 6, "Runtime finish task"));
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach (var sql in new[] { "UPDATE app.execution_tasks SET step_id=gen_random_uuid()", "DELETE FROM app.execution_task_events", "DELETE FROM app.playbook_executions" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
                var cancelled = await service.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(4, 7, "Runtime cancel"));
                Assert.Equal("CANCELLED", cancelled.Status);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync();
        }
    }
}
