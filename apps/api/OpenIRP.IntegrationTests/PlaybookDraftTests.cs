using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<T> Playbooks<T>(Func<IPlaybookDraftService, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPlaybookDraftService>());
    }

    private async Task<PlaybookDraftContent> DraftContent() => new("Procedimiento privado", await IncidentCategoryId(),
        "Objetivo confidencial", "Instrucciones privadas", [
            new("DETECT", 10, "Análisis", "Preservar antecedentes", true, false, true),
            new("CONTAIN", 20, "Contención", "Aislamiento autorizado", true, true, true),
            new("RECOVER", 30, "Recuperación", "Validar recuperación", false, false, false)],
        [new("DETECT", "CONTAIN"), new("CONTAIN", "RECOVER")],
        [new(await CapabilityId(), null, PlaybookRequirementLevel.Required),
         new(await CapabilityId(), "CONTAIN", PlaybookRequirementLevel.Recommended)]);

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybookDraftCreationAndReplacementKeepStableAggregateAndMinimalAudit(bool global)
    {
        var seed = await Seed(); var actor = global ? seed.Platform : seed.Admin; Guid? org = global ? null : seed.Org.Id;
        var content = await DraftContent();
        var item = await Playbooks(s => s.CreateAsync(actor, org, new(content)));
        Assert.Equal(global ? "PUBLIC" : "PRIVATE", item.Playbook.Visibility); Assert.Equal(org, item.Playbook.OrganizationId);
        Assert.Equal(1, item.VersionNumber); Assert.Equal("DRAFT", item.Status); Assert.Equal(1, item.Playbook.Revision);
        Assert.Equal(3, item.Steps.Count); Assert.Equal(2, item.Dependencies.Count); Assert.Equal(2, item.Requirements.Count);
        Assert.Contains(item.Requirements, r => r.StepId == null); Assert.Contains(item.Requirements, r => r.StepId != null);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "playbook.draft_created"));
        Assert.Equal(org, audit.OrganizationId); Assert.Equal(item.Playbook.Id, audit.ResourceId);
        Assert.DoesNotContain(content.Objective, JsonSerializer.Serialize(audit));
        var changed = content with { Title = "Título corregido", Steps = [content.Steps[0]], Dependencies = [],
            Requirements = [content.Requirements[0]], Instructions = "Revisión privada" };
        var updated = await Playbooks(s => s.UpdateAsync(actor, org, item.Playbook.Id, new(changed, "Corrección confidencial", 1)));
        Assert.Equal(2, updated.Playbook.Revision); Assert.Equal(item.VersionId, updated.VersionId);
        Assert.DoesNotContain(updated.Steps[0].Id, item.Steps.Select(s => s.Id));
        Assert.Empty(updated.Dependencies); Assert.Single(updated.Requirements);
        Assert.Equal(JsonSerializer.Serialize(updated), JsonSerializer.Serialize(await Playbooks(s => s.GetAsync(actor, org, item.Playbook.Id))));
        Assert.Single((await Playbooks(s => s.ListAsync(actor, org))).Items);
        var audits = await InScope((_, db) => db.AuditEvents.Where(e => e.Action.StartsWith("playbook.")).ToListAsync());
        Assert.Equal(2, audits.Count); Assert.DoesNotContain("confidencial", JsonSerializer.Serialize(audits));
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task PlaybookDraftScopesNeverExposeAnotherOrganizationOrPublicDraft()
    {
        var seed = await Seed(); var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaActor = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var content = await DraftContent();
        var a = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var b = await Playbooks(s => s.CreateAsync(betaActor, beta.Id, new(content)));
        var p = await Playbooks(s => s.CreateAsync(seed.Platform, null, new(content)));
        Assert.Equal(a.Playbook.Id, Assert.Single((await Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id))).Items).Id);
        await Rejected(404, () => Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, b.Playbook.Id)));
        await Rejected(404, () => Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, b.Playbook.Id, new(content, "Cruce", 1))));
        await Rejected(404, () => Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, p.Playbook.Id)));
        await Rejected(404, () => Playbooks(s => s.GetAsync(seed.Platform, null, a.Playbook.Id)));
        await Rejected(403, () => Playbooks(s => s.GetAsync(seed.Platform, seed.Org.Id, a.Playbook.Id)));
        await Rejected(403, () => Playbooks(s => s.GetAsync(seed.Admin, null, p.Playbook.Id)));
        Assert.Equal(3, await InScope((_, db) => db.Playbooks.CountAsync()));
    }

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task NonAdministratorsCannotReadOrEditPrivateDraftsEvenWithGlobalGrant(OrganizationRole role)
    {
        var seed = await Seed(); await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var content = await DraftContent(); var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        await Rejected(403, () => Playbooks(s => s.ListAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => Playbooks(s => s.GetAsync(seed.Platform, seed.Org.Id, item.Playbook.Id)));
        await Rejected(403, () => Playbooks(s => s.CreateAsync(seed.Platform, seed.Org.Id, new(content))));
        await Rejected(403, () => Playbooks(s => s.UpdateAsync(seed.Platform, seed.Org.Id, item.Playbook.Id, new(content, "Cambio", 1))));
    }

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData("account")]
    [InlineData("lockout")]
    [InlineData("organization")]
    [InlineData("membership")]
    [InlineData("grant")]
    public async Task PlaybookDraftAuthorizationIsRevalidatedForReadsAndWrites(string condition)
    {
        var seed = await Seed(); var global = condition == "grant"; Guid? org = global ? null : seed.Org.Id;
        var actor = global ? seed.Platform : seed.Admin;
        var content = await DraftContent(); var item = await Playbooks(s => s.CreateAsync(actor, org, new(content)));
        await InScope(async (_, db) =>
        {
            if (condition == "account") (await db.Users.SingleAsync(u => u.Id == actor)).IsActive = false;
            else if (condition == "lockout") (await db.Users.SingleAsync(u => u.Id == actor)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            else if (condition == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else if (condition == "membership") (await db.OrganizationMemberships.SingleAsync()).Status = MembershipStatus.Suspended;
            else (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Playbooks(s => s.GetAsync(actor, org, item.Playbook.Id)));
        await Rejected(403, () => Playbooks(s => s.UpdateAsync(actor, org, item.Playbook.Id, new(content, "Cambio", 1))));
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.Select(p => p.Revision).SingleAsync()));
    }

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData("cycle")]
    [InlineData("self")]
    [InlineData("foreign")]
    [InlineData("duplicate_edge")]
    [InlineData("duplicate_code")]
    [InlineData("duplicate_position")]
    [InlineData("bad_code")]
    [InlineData("missing_step")]
    [InlineData("duplicate_requirement")]
    [InlineData("bad_level")]
    [InlineData("null_steps")]
    [InlineData("null_element")]
    [InlineData("too_many")]
    public async Task InvalidPlaybookDraftGraphNeverPartiallyReplacesContent(string invalid)
    {
        var seed = await Seed(); var content = await DraftContent();
        var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var bad = invalid switch
        {
            "cycle" => content with { Dependencies = [..content.Dependencies, new("RECOVER", "DETECT")] },
            "self" => content with { Dependencies = [new("DETECT", "DETECT")] },
            "foreign" => content with { Dependencies = [new("DETECT", "OTHER")] },
            "duplicate_edge" => content with { Dependencies = [content.Dependencies[0], content.Dependencies[0]] },
            "duplicate_code" => content with { Steps = [content.Steps[0], content.Steps[1] with { Code = "DETECT" }] },
            "duplicate_position" => content with { Steps = [content.Steps[0], content.Steps[1] with { Position = 10 }] },
            "bad_code" => content with { Steps = [content.Steps[0] with { Code = "bad code" }] },
            "missing_step" => content with { Requirements = [content.Requirements[0] with { StepCode = "OTHER" }] },
            "duplicate_requirement" => content with { Requirements = [content.Requirements[0], content.Requirements[0]] },
            "bad_level" => content with { Requirements = [content.Requirements[0] with { Level = (PlaybookRequirementLevel)99 }] },
            "null_steps" => content with { Steps = null! },
            "null_element" => content with { Steps = [null!] },
            _ => content with { Steps = Enumerable.Repeat(content.Steps[0], 101).ToArray() }
        };
        await Rejected(400, () => Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(bad, "Inválido", 1))));
        Assert.Equal(JsonSerializer.Serialize(item), JsonSerializer.Serialize(await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Playbook.Id))));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action.StartsWith("playbook.") && e.Outcome == "SUCCESS")));
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task DraftsPermitEmptyGraphButValidateReferencesReasonsRevisionsAndPagination()
    {
        var seed = await Seed(); var content = await DraftContent();
        content = content with { Steps = [], Dependencies = [], Requirements = [] };
        var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        Assert.Empty(item.Steps);
        await Rejected(400, () => Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content with { CategoryId = Guid.NewGuid() }))));
        await Rejected(409, () => Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content with { Requirements = [new(Guid.NewGuid(), null, PlaybookRequirementLevel.Required)] }))));
        await Rejected(400, () => Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(content, " ", 1))));
        await Rejected(409, () => Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(content, "Viejo", 0))));
        await Rejected(400, () => Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id, -1)));
        await Rejected(400, () => Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id, 0, 101)));
        await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        var page = await Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id, 0, 1));
        Assert.Single(page.Items); Assert.Equal(1, page.NextSkip);
        Assert.Null((await Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id, 1, 1))).NextSkip);
    }

    [Fact, Trait("Feature", "PlaybookDrafts")]
    public async Task ConcurrentDraftEditsCommitExactlyOneGraphAndOneRevision()
    {
        var seed = await Seed(); var content = await DraftContent();
        var item = await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        async Task<int> Edit(string title)
        {
            try { await Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(content with { Title = title }, "Concurrente", 1))); return 200; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Edit("Uno"), () => Edit("Dos"))).Order().ToArray());
        var current = await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Playbook.Id));
        Assert.Equal(2, current.Playbook.Revision); Assert.Equal(3, current.Steps.Count);
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action.StartsWith("playbook.") && e.Outcome == "SUCCESS")));
    }

    [Theory, Trait("Feature", "PlaybookDrafts")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftAuditFailureRollsBackCreationOrDeletedGraph(bool update)
    {
        var seed = await Seed(); var content = await DraftContent();
        var before = update ? await Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content))) : null;
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION audit.reject_playbook_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected playbook audit failure'; END; $body$;
            CREATE TRIGGER reject_playbook_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS' AND NEW.action LIKE 'playbook.%') EXECUTE FUNCTION audit.reject_playbook_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(() => update
            ? Playbooks(s => s.UpdateAsync(seed.Admin, seed.Org.Id, before!.Playbook.Id,
                new(content with { Steps = [], Dependencies = [], Requirements = [] }, "Vaciar", 1)))
            : Playbooks(s => s.CreateAsync(seed.Admin, seed.Org.Id, new(content))));
        if (update) Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, before!.Playbook.Id))));
        else
        {
            Assert.Equal(0, await InScope((_, db) => db.Playbooks.CountAsync()));
            Assert.Equal(0, await InScope((_, db) => db.PlaybookVersions.CountAsync()));
            Assert.Equal(0, await InScope((_, db) => db.PlaybookSteps.CountAsync()));
        }
    }
}
