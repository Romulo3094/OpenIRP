using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [Fact, Trait("Feature", "PlaybookCloning")]
    public async Task CloneIsIndependentAfterSourceWithdrawalAndKeepsOriginAcrossItsOwnVersions()
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        var clone = await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source)));
        await Playbooks(s => s.WithdrawAsync(seed.Platform, null, source.Playbook.Id, source.VersionId, new("Retirada posterior", 3)));
        clone = await DecidePlaybook(seed.Admin, seed.Org.Id, await SubmitPlaybook(seed.Admin, seed.Org.Id, clone));
        var next = await Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id, new(clone.VersionId, "Evolución privada", 3)));
        Assert.Equal(clone.VersionId, next.SourceVersionId); Assert.Equal(source.VersionId, next.Playbook.SourceVersionId);
        Assert.Equal(3, next.Playbook.SourceRevision);
        Assert.Equal(clone.VersionId, (await Playbooks(s => s.CurrentAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id))).VersionId);
        Assert.Equal(4, await InScope((_, db) => db.PlaybookReviews.CountAsync(r => r.Action != "WITHDRAWN")));
        Assert.Equal(0, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("audit")]
    [InlineData("steps")]
    public async Task CloneFailureRollsBackNewAggregateAllNodesAndSuccessAudit(string failure)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(failure == "audit" ? """
            CREATE FUNCTION audit.reject_clone_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected clone audit failure'; END; $body$;
            CREATE TRIGGER reject_clone_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.action='playbook.cloned' AND NEW.outcome='SUCCESS') EXECUTE FUNCTION audit.reject_clone_test();
            """ : """
            CREATE FUNCTION app.reject_clone_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected clone content failure'; END; $body$;
            CREATE TRIGGER reject_clone_test BEFORE INSERT ON app.playbook_steps FOR EACH ROW
            EXECUTE FUNCTION app.reject_clone_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(() => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source))));
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.CountAsync())); Assert.Equal(1, await InScope((_, db) => db.PlaybookVersions.CountAsync()));
        Assert.Equal(3, await InScope((_, db) => db.PlaybookSteps.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.PlaybookStepDependencies.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.PlaybookCapabilityRequirements.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action == "playbook.cloned" && e.Outcome == "SUCCESS")));
        Assert.Equal(source.Playbook.Revision, (await Playbooks(s => s.CurrentAsync(seed.Platform, null, source.Playbook.Id))).Playbook.Revision);
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("version")]
    [InlineData("revision")]
    [InlineData("reason")]
    public async Task SqlRejectsChangesToCloneOriginEvenAfterSourceWithdrawal(string field)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        var clone = await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source)));
        await Playbooks(s => s.WithdrawAsync(seed.Platform, null, source.Playbook.Id, source.VersionId, new("Retirar fuente", 3)));
        var sql = field switch
        {
            "version" => "UPDATE app.playbooks SET revision=revision+1,source_version_id=NULL,source_revision=NULL,clone_reason=NULL WHERE source_version_id IS NOT NULL",
            "revision" => "UPDATE app.playbooks SET revision=revision+1,source_revision=source_revision+1 WHERE source_version_id IS NOT NULL",
            _ => "UPDATE app.playbooks SET revision=revision+1,clone_reason='Procedencia alterada' WHERE source_version_id IS NOT NULL"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) =>
            db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(source.VersionId, (await Playbooks(s => s.OriginAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id))).SourceVersionId);
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("private_public", "23514")]
    [InlineData("private_other", "23514")]
    [InlineData("stale", "23514")]
    [InlineData("draft", "23514")]
    [InlineData("missing", "23503")]
    public async Task SqlCloneOriginRequiresMatchingCurrentSourceAndCompatibleOrganization(string invalid, string expected)
    {
        var seed = await Seed(); var source = invalid == "draft"
            ? await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(await DraftContent())))
            : await PublishedPlaybook(seed.Admin, seed.Org.Id);
        Guid? destination = invalid == "private_public" ? null : seed.Org.Id;
        if (invalid == "private_other") destination = (await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))))).Id;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.Playbooks.Add(new() { OrganizationId = destination, CategoryId = source.Playbook.CategoryId,
                Title = "Invalid clone", CreatedByUserId = seed.Admin, SourceVersionId = invalid == "missing" ? Guid.NewGuid() : source.VersionId,
                SourceRevision = invalid == "stale" ? source.Playbook.Revision - 1 : source.Playbook.Revision, CloneReason = "Raw insert" });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal(expected, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.CountAsync()));
    }

    [Fact, Trait("Feature", "PlaybookCloning")]
    public async Task CloneMigrationPreservesPreviousPlaybooksAndRefusesLosingProvenance()
    {
        var seed = await Seed(); var playbook = Guid.NewGuid(); var version = Guid.NewGuid(); var category = await IncidentCategoryId();
        await InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261024010838_PlaybookPublication");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.playbooks (id,organization_id,category_id,title,revision,created_by_user_id,created_at) VALUES ({playbook},{seed.Org.Id},{category},'Anterior',1,{seed.Admin},{DateTimeOffset.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.playbook_versions (id,playbook_id,version_number,status,objective,instructions,title,category_id) VALUES ({version},{playbook},1,'DRAFT','Objetivo','Instrucciones','Anterior',{category})");
            await db.Database.MigrateAsync(); return true;
        });
        var origin = await Playbooks(s => s.OriginAsync(seed.Admin, seed.Org.Id, playbook));
        Assert.Null(origin.SourceVersionId); Assert.Null(origin.SourceRevision); Assert.Null(origin.Reason);
        Assert.Equal(version, (await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, playbook))).VersionId);
        var source = await PublishedPlaybook(seed.Platform, null);
        var clone = await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) =>
            { await db.GetService<IMigrator>().MigrateAsync("20261024010838_PlaybookPublication"); return true; }));
        Assert.Contains("Cannot roll back playbook cloning", error.MessageText);
        Assert.Equal(source.VersionId, (await Playbooks(s => s.OriginAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id))).SourceVersionId);
    }

    [Fact, Trait("Feature", "PlaybookCloning")]
    public async Task HttpCloneRequiresSourceRevisionCsrfAndUsesDestinationRouteRatherThanBody()
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        var root = $"/api/organizations/{seed.Org.Id}/playbooks";
        using var admin = Client(); await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(root + "/clones", new
            { sourcePlaybookId = source.Playbook.Id, sourceVersionId = source.VersionId, reason = "Falta revisión" })).StatusCode);
        var created = await admin.PostAsJsonAsync(root + "/clones", new
        {
            sourcePlaybookId = source.Playbook.Id, sourceVersionId = source.VersionId,
            expectedSourceRevision = source.Playbook.Revision, reason = "Copiar HTTP",
            organizationId = Guid.NewGuid(), visibility = "PUBLIC"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter());
        var clone = (await created.Content.ReadFromJsonAsync<PlaybookDraftItem>(options))!;
        Assert.Equal(seed.Org.Id, clone.Playbook.OrganizationId); Assert.Equal("PRIVATE", clone.Playbook.Visibility);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(created.Headers.Location)).StatusCode);
        var origin = await admin.GetFromJsonAsync<PlaybookOriginItem>(root + $"/{clone.Playbook.Id}/origin");
        Assert.Equal("Copiar HTTP", origin!.Reason);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(root + "/clones", CloneRequest(source))).StatusCode);
        using var anon = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(root + "/clones", CloneRequest(source))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(root + $"/{clone.Playbook.Id}/origin")).StatusCode);
        await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/platform/playbooks/clones", CloneRequest(source))).StatusCode);
    }

    [Fact, Trait("Feature", "PlaybookCloning")]
    public async Task SqlRuntimeCanCloneWithoutReceivingUpdatePermissionOnOrigin()
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        var role = "openirp_clone_runtime_" + Guid.NewGuid().ToString("N");
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
                using var scope = runtime.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IPlaybookDraftService>();
                var clone = await service.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source));
                Assert.Equal(source.VersionId, (await service.OriginAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id)).SourceVersionId);
                await service.UpdateAsync(seed.Admin, seed.Org.Id, clone.Playbook.Id, new(ContentForTest(clone) with { Title = "Runtime clone" }, "Editar", 1));
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE app.playbooks SET source_revision=1"));
                Assert.Equal("42501", error.SqlState);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync();
        }
    }
}
