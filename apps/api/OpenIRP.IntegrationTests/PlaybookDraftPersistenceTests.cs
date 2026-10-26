using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task DraftMigrationUpgradesExistingDataAndRejectsLossyRollback()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261023220910_IncidentLifecycleClosure"); await db.Database.MigrateAsync(); return true; });
        Assert.Equal(incident.Id, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Id);
        var content = await DraftContent(); var draft = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) =>
            { await db.GetService<IMigrator>().MigrateAsync("20261023220910_IncidentLifecycleClosure"); return true; }));
        Assert.Contains("Cannot roll back playbook drafts", error.MessageText);
        // Earlier Down steps may commit before the older data-loss guard rejects the target.
        await InScope(async (_, db) => { await db.Database.MigrateAsync(); return true; });
        Assert.Equal(draft.VersionId, (await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, draft.Playbook.Id))).VersionId);
    }

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData("scope", "23514")]
    [InlineData("revision", "23514")]
    [InlineData("version_owner", "23514")]
    [InlineData("publish", "23514")]
    [InlineData("delete", "42501")]
    [InlineData("truncate", "42501")]
    public async Task DatabaseProtectsDraftIdentityAndDoesNotAllowPrematurePublication(string operation, string sqlState)
    {
        var seed = await Seed(); var content = await DraftContent();
        await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var query = operation switch
        {
            "scope" => "UPDATE app.playbooks SET organization_id=NULL,revision=revision+1",
            "revision" => "UPDATE app.playbooks SET title='Sin revisión'",
            "version_owner" => "UPDATE app.playbook_versions SET playbook_id=gen_random_uuid()",
            "publish" => "UPDATE app.playbook_versions SET status='PUBLISHED'",
            "delete" => "DELETE FROM app.playbook_versions",
            _ => "TRUNCATE app.playbook_step_dependencies,app.playbook_capability_requirements,app.playbook_steps,app.playbook_versions,app.playbooks,app.playbook_reviews,app.playbook_executions,app.execution_tasks,app.execution_task_events,app.step_approvals,app.incident_evidence,app.evidence_events,app.evidence_files,app.evidence_file_events"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(query)));
        Assert.Equal(sqlState, error.SqlState);
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task CompositeVersionReferencesBlockCrossPlaybookDependenciesAndRequirements()
    {
        var seed = await Seed(); var content = await DraftContent();
        var a = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var b = await Playbooks(s => s.CreateAsync(seed.Platform, null, new(content)));
        var dependency = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookStepDependencies.Add(new() { VersionId = a.VersionId, PredecessorId = b.Steps[0].Id, SuccessorId = a.Steps[0].Id });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(dependency.InnerException).SqlState);
        var requirement = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookCapabilityRequirements.Add(new() { VersionId = a.VersionId, StepId = b.Steps[0].Id,
                CapabilityId = content.Requirements[0].CapabilityId, Level = PlaybookRequirementLevel.Required });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(requirement.InnerException).SqlState);
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookCapabilityRequirements.Add(new() { VersionId = a.VersionId,
                CapabilityId = content.Requirements[0].CapabilityId, Level = PlaybookRequirementLevel.Recommended });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23505", Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task InactiveCatalogEntriesCannotIntroduceNewDraftReferences()
    {
        var seed = await Seed(); var content = await DraftContent();
        var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        await InScope(async (_, db) =>
        {
            (await db.IncidentCategories.SingleAsync(c => c.Id == content.CategoryId)).IsActive = false;
            (await db.CapabilityCatalog.SingleAsync(c => c.Id == content.Requirements[0].CapabilityId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        await Rejected(409, () => Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content))));
        await Rejected(409, () => Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(content, "No conserva capacidad inactiva", 1))));
        var removed = content with { Requirements = [] };
        Assert.Equal(2, (await Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(removed, "Retirar capacidad inactiva", 1)))).Playbook.Revision);
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task HttpDraftRoutesRequireAuthenticationCsrfExplicitFlagsAndRevision()
    {
        var seed = await Seed(); var content = await DraftContent();
        var root = $"/api/organizations/{seed.Org.Id}/playbooks";
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/platform/playbooks")).StatusCode);
        using var admin = Client(); await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/platform/playbooks")).StatusCode);
        var created = await admin.PostAsJsonAsync(root, new { content, organizationId = Guid.NewGuid(), visibility = "PUBLIC" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<PlaybookDraftItem>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;
        Assert.Equal(seed.Org.Id, item.Playbook.OrganizationId); Assert.Equal("PRIVATE", item.Playbook.Visibility);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(created.Headers.Location)).StatusCode);
        var url = root + $"/{item.Playbook.Id}/draft";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new { content, reason = "Falta revisión" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(root, new { content = new
        {
            content.Title, content.CategoryId, content.Objective, content.Instructions,
            steps = new[] { new { code = "ONE", position = 1, phase = "Análisis", instructions = "Paso" } },
            dependencies = Array.Empty<object>(), requirements = Array.Empty<object>()
        } })).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new UpdatePlaybookDraft(content, "Sin CSRF", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync(root + $"/{item.Playbook.Id}")).StatusCode);
        using var platform = Client(); await Login(platform, "platform");
        Assert.Equal(HttpStatusCode.Created, (await platform.PostAsJsonAsync("/api/platform/playbooks", new CreatePlaybookDraft(content))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync(root)).StatusCode);
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task RestrictedSqlRuntimeCanReplaceDraftsButCannotChangeScopeOrPublish()
    {
        var seed = await Seed(); var content = await DraftContent();
        var role = "openirp_pb_runtime_" + Guid.NewGuid().ToString("N");
        var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role', @role, false)", owner))
            { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner))
                await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using (var runtime = new TestApplication(builder.ConnectionString))
            {
                using var scope = runtime.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPlaybookDraftService>();
                var item = await service.CreateAsync(seed.Admin, seed.Org.Id, new(content));
                var updated = await service.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id,
                    new(content with { Steps = [], Dependencies = [], Requirements = [] }, "Reemplazo", 1));
                Assert.Equal(2, updated.Playbook.Revision); Assert.Empty(updated.Steps);
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach (var sql in new[] { "UPDATE app.playbooks SET organization_id=NULL",
                    "UPDATE app.playbook_versions SET status='PUBLISHED'", "DELETE FROM app.playbooks" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal(sql.Contains("status=") ? "23514" : "42501", error.SqlState);
                }
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
