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
    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("review", "version")]
    [InlineData("review", "step")]
    [InlineData("review", "dependency")]
    [InlineData("review", "requirement")]
    [InlineData("published", "version")]
    [InlineData("published", "step")]
    [InlineData("published", "dependency")]
    [InlineData("published", "requirement")]
    [InlineData("archived", "version")]
    [InlineData("archived", "step")]
    [InlineData("archived", "dependency")]
    [InlineData("archived", "requirement")]
    public async Task SqlFreezesReviewedPublishedAndArchivedContent(string state, string table)
    {
        var seed = await Seed(); var content = await DraftContent();
        var item = await SubmitPlaybook(seed.Admin, seed.Org.Id, await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content))));
        if (state != "review") item = await DecidePlaybook(seed.Admin, seed.Org.Id, item);
        if (state == "archived") item = await Playbooks(s => s.WithdrawAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId, new("Retirada", 3)));
        var sql = table switch
        {
            "version" => "UPDATE app.playbook_versions SET objective='Alterado'",
            "step" => "UPDATE app.playbook_steps SET is_required=false",
            "dependency" => "DELETE FROM app.playbook_step_dependencies",
            _ => "DELETE FROM app.playbook_capability_requirements"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(3, (await Playbooks(s => s.VersionAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId))).Steps.Count);
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task SqlBlocksInsertingIntoPublishedGraphAndChangingPointerToDraftOrAnotherPlaybook()
    {
        var seed = await Seed(); var published = await PublishedPlaybook(seed.Admin, seed.Org.Id);
        var draft = await Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, published.Playbook.Id, new(published.VersionId, "Nueva", 3)));
        var inserted = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.PlaybookSteps.Add(new() { VersionId = published.VersionId, Code = "NEW", Position = 999, Phase = "Otra", Instructions = "Agregado" });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(inserted.InnerException).SqlState);
        var pointer = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.playbooks SET current_published_version_id={draft.VersionId},revision=revision+1 WHERE id={published.Playbook.Id}")));
        Assert.Equal("23514", pointer.SqlState);
        var other = await PublishedPlaybook(seed.Platform, null);
        var cross = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.playbooks SET current_published_version_id={other.VersionId},revision=revision+1 WHERE id={published.Playbook.Id}")));
        Assert.Equal("23503", cross.SqlState);
        var archive = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.playbook_versions SET status='ARCHIVED' WHERE id={published.VersionId}")));
        Assert.Equal("23514", archive.SqlState);
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("UPDATE app.playbook_reviews SET reason='Alterado'")]
    [InlineData("DELETE FROM app.playbook_reviews")]
    [InlineData("TRUNCATE app.playbook_reviews")]
    public async Task SqlPublicationReviewHistoryIsAppendOnly(string sql)
    {
        var seed = await Seed(); await PublishedPlaybook(seed.Admin, seed.Org.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("42501", error.SqlState);
        Assert.Equal(2, await InScope((_, db) => db.PlaybookReviews.CountAsync()));
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("submit")]
    [InlineData("publish")]
    [InlineData("withdraw")]
    [InlineData("new_version")]
    public async Task PublicationAuditFailureRollsBackVersionPointerAndReviewTogether(string operation)
    {
        var seed = await Seed(); var content = await DraftContent();
        var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        if (operation != "submit") item = await SubmitPlaybook(seed.Admin, seed.Org.Id, item);
        if (operation is "withdraw" or "new_version") item = await DecidePlaybook(seed.Admin, seed.Org.Id, item);
        var before = await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Playbook.Id));
        var count = await InScope((_, db) => db.PlaybookReviews.CountAsync());
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION audit.reject_publication_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected publication audit failure'; END; $body$;
            CREATE TRIGGER reject_publication_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS' AND NEW.action LIKE 'playbook.%') EXECUTE FUNCTION audit.reject_publication_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (operation == "submit") await SubmitPlaybook(seed.Admin, seed.Org.Id, item);
            else if (operation == "publish") await DecidePlaybook(seed.Admin, seed.Org.Id, item);
            else if (operation == "withdraw") await Playbooks(s => s.WithdrawAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId, new("Retirar", item.Playbook.Revision)));
            else await Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(item.VersionId, "Nueva", item.Playbook.Revision)));
        });
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Playbook.Id))));
        Assert.Equal(count, await InScope((_, db) => db.PlaybookReviews.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.PlaybookVersions.CountAsync()));
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task PublicationMigrationBackfillsExistingDraftMetadataWithoutPublishingOrRewritingSteps()
    {
        var seed = await Seed(); var playbook = Guid.NewGuid(); var version = Guid.NewGuid(); var step = Guid.NewGuid(); var category = await IncidentCategoryId();
        await InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261023231051_PlaybookDraftCatalog");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.playbooks (id,organization_id,category_id,title,revision,created_by_user_id,created_at) VALUES ({playbook},{seed.Org.Id},{category},'Borrador anterior',1,{seed.Admin},{DateTimeOffset.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.playbook_versions (id,playbook_id,version_number,status,objective,instructions) VALUES ({version},{playbook},1,'DRAFT','Objetivo anterior','Instrucción anterior')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.playbook_steps (id,version_id,code,position,phase,instructions,is_required,requires_approval,requires_evidence) VALUES ({step},{version},'OLD',10,'Análisis','Paso anterior',true,false,true)");
            await db.Database.MigrateAsync(); return true;
        });
        var item = await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, playbook));
        Assert.Equal("Borrador anterior", item.Playbook.Title); Assert.Equal(category, item.Playbook.CategoryId);
        Assert.Equal(step, Assert.Single(item.Steps).Id); Assert.Null(item.PublishedAt); Assert.Null(item.ReviewRequestId);
        Assert.Null(item.Playbook.CurrentPublishedVersionId); Assert.Equal(1, item.Playbook.Revision);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) =>
            { await db.GetService<IMigrator>().MigrateAsync("20261023231051_PlaybookDraftCatalog"); return true; }));
        Assert.Contains("Cannot roll back playbook drafts/publication", error.MessageText);
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task PublicationRejectsCrossVersionScopeStaleCommandsAndInvalidPaging()
    {
        var seed = await Seed(); var a = await PublishedPlaybook(seed.Admin, seed.Org.Id);
        var b = await PublishedPlaybook(seed.Platform, null);
        await Rejected(404, () => Playbooks(s => s.VersionAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, b.VersionId)));
        await Rejected(404, () => Playbooks(s => s.WithdrawAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, b.VersionId, new("Cruce", 3))));
        await Rejected(404, () => Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, new(b.VersionId, "Origen ajeno", 3))));
        await Rejected(403, () => Playbooks(s => s.ReviewsAsync(seed.Platform, seed.Org.Id, a.Playbook.Id)));
        await Rejected(409, () => Playbooks(s => s.WithdrawAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, a.VersionId, new("Obsoleto", 1))));
        await Rejected(400, () => Playbooks(s => s.VersionsAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, -1)));
        await Rejected(400, () => Playbooks(s => s.ReviewsAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, 0, 101)));
        await Rejected(400, () => Playbooks(s => s.ListCurrentAsync(seed.Admin, seed.Org.Id, 0, 101)));
        Assert.Single(await Playbooks(s => s.ReviewsAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, 2, 1)));
        Assert.Empty(await Playbooks(s => s.VersionsAsync(seed.Admin, seed.Org.Id, a.Playbook.Id, 1)));
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task HttpPublicationRequiresCsrfRevisionDecisionFlagsAndHidesDraftsFromPublishedReaders()
    {
        var seed = await Seed(); var content = await DraftContent(); var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var root = $"/api/organizations/{seed.Org.Id}/playbooks/{item.Playbook.Id}";
        using var admin = Client(); await Login(admin, "alpha");
        var submit = await admin.PostAsJsonAsync(root + $"/versions/{item.VersionId}/review-requests", new PlaybookVersionCommand("Revisión HTTP", 1));
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new JsonStringEnumConverter());
        var pending = (await submit.Content.ReadFromJsonAsync<PlaybookDraftItem>(options))!;
        var decisionUrl = root + $"/versions/{item.VersionId}/review-requests/{pending.ReviewRequestId}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(decisionUrl, new { publish = true, reason = "Faltan flags", expectedRevision = 2 })).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(decisionUrl, new DecidePlaybookPublication(true, true, "Excepción", "Sin CSRF", 2))).StatusCode);
        await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(decisionUrl, new DecidePlaybookPublication(true, true, "Excepción HTTP", "Publicar", 2))).StatusCode);
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        using var viewer = Client(); await Login(viewer, "platform");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(root + "/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(root + "/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/organizations/{seed.Org.Id}/playbooks/published")).StatusCode);
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(root + "/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/playbooks/public")).StatusCode);
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task RestrictedRuntimeCanPublishWithdrawAndCreateNewVersionWhileHistoryAndContentRemainProtected()
    {
        var seed = await Seed(); var content = await DraftContent();
        var role = "openirp_publish_runtime_" + Guid.NewGuid().ToString("N");
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
                var item = await service.CreateAsync(seed.Admin, seed.Org.Id, new(content));
                item = await service.SubmitAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId, new("Enviar", 1));
                item = await service.DecideAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId, item.ReviewRequestId!.Value, new(true, true, "Excepción controlada", "Publicar", 2));
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                var modify = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE app.playbook_versions SET instructions='Alterada'"));
                Assert.Equal("23514", modify.SqlState);
                var mutateLog = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM app.playbook_reviews"));
                Assert.Equal("42501", mutateLog.SqlState);
                item = await service.WithdrawAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId, new("Retirar", 3));
                var next = await service.NewVersionAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(item.VersionId, "Nueva", 4));
                Assert.Equal(2, next.VersionNumber); Assert.Null(next.Playbook.CurrentPublishedVersionId);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync();
        }
    }
}
