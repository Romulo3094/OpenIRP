using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Playbooks;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private static ClonePlaybook CloneRequest(PlaybookDraftItem source, string reason = "Adaptación privada autorizada") =>
        new(source.Playbook.Id, source.VersionId, source.Playbook.Revision, reason);

    private static string CloneContentJson(PlaybookDraftItem item)
    {
        var content = ContentForTest(item);
        return JsonSerializer.Serialize(content with
        {
            Dependencies = content.Dependencies.OrderBy(d => d.PredecessorCode).ThenBy(d => d.SuccessorCode).ToList(),
            Requirements = content.Requirements.OrderBy(r => r.StepCode).ThenBy(r => r.CapabilityId).ToList()
        });
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("public_private")]
    [InlineData("public_public")]
    [InlineData("private_private")]
    public async Task AuthorizedCloneCopiesEntireGraphWithNewIdentityAndNoInheritedApproval(string mode)
    {
        var seed = await Seed(); var privateSource = mode == "private_private"; Guid? sourceOrg = privateSource ? seed.Org.Id : null;
        var sourceActor = privateSource ? seed.Admin : seed.Platform;
        var source = await PublishedPlaybook(sourceActor, sourceOrg);
        Guid? destination = mode == "public_public" ? null : seed.Org.Id;
        var actor = destination is null ? seed.Platform : seed.Admin;
        var clone = await Playbooks(s => s.CloneAsync(actor, destination, CloneRequest(source)));
        Assert.NotEqual(source.Playbook.Id, clone.Playbook.Id); Assert.NotEqual(source.VersionId, clone.VersionId);
        Assert.Equal(destination, clone.Playbook.OrganizationId); Assert.Equal("DRAFT", clone.Status);
        Assert.Equal(1, clone.Playbook.Revision); Assert.Equal(1, clone.VersionNumber);
        Assert.Null(clone.PublishedAt); Assert.Null(clone.ReviewRequestId); Assert.Null(clone.SourceVersionId);
        Assert.Null(clone.Playbook.CurrentPublishedVersionId); Assert.Equal(source.VersionId, clone.Playbook.SourceVersionId);
        Assert.Equal(source.Playbook.Revision, clone.Playbook.SourceRevision);
        Assert.Equal(CloneContentJson(source), CloneContentJson(clone));
        Assert.Empty(source.Steps.Select(s => s.Id).Intersect(clone.Steps.Select(s => s.Id)));
        Assert.Empty(source.Dependencies.Select(s => s.Id).Intersect(clone.Dependencies.Select(s => s.Id)));
        Assert.Empty(source.Requirements.Select(s => s.Id).Intersect(clone.Requirements.Select(s => s.Id)));
        Assert.Empty(await Playbooks(s => s.ReviewsAsync(actor, destination, clone.Playbook.Id)));
        var origin = await Playbooks(s => s.OriginAsync(actor, destination, clone.Playbook.Id));
        Assert.Equal(source.Playbook.Id, origin.SourcePlaybookId); Assert.Equal(source.VersionId, origin.SourceVersionId);
        Assert.Equal(CloneRequest(source).Reason, origin.Reason); Assert.Equal(actor, origin.CreatedByUserId);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "playbook.cloned"));
        Assert.Equal(destination, audit.OrganizationId); Assert.Equal(clone.Playbook.Id, audit.ResourceId);
        Assert.DoesNotContain(CloneRequest(source).Reason, JsonSerializer.Serialize(audit));
        Assert.DoesNotContain(source.Instructions, JsonSerializer.Serialize(audit));
        var currentSource = await Playbooks(s => s.CurrentAsync(sourceActor, sourceOrg, source.Playbook.Id));
        Assert.Equal(source.Playbook.Revision, currentSource.Playbook.Revision);
        Assert.Equal(CloneContentJson(source), CloneContentJson(currentSource));
        var edited = await Playbooks(s => s.UpdateAsync(actor, destination, clone.Playbook.Id,
            new(ContentForTest(clone) with { Title = "Copia editable", Steps = [], Dependencies = [], Requirements = [] }, "Modificar copia", 1)));
        Assert.Empty(edited.Steps); Assert.Equal(source.VersionId, edited.Playbook.SourceVersionId);
        Assert.Equal(3, (await Playbooks(s => s.CurrentAsync(sourceActor, sourceOrg, source.Playbook.Id))).Steps.Count);
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("private_public")]
    [InlineData("different_org")]
    [InlineData("dual_admin")]
    public async Task CloningNeverExportsPrivateContentAcrossOrganizationsOrToPublicCatalog(string mode)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Admin, seed.Org.Id);
        Guid? destination = null; var actor = seed.Platform;
        if (mode != "private_public")
        {
            var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
            destination = beta.Id;
            actor = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
            if (mode == "dual_admin") await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(actor, OrganizationRole.OrgAdmin)));
        }
        else await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        await Rejected(404, () => Playbooks(s => s.CloneAsync(actor, destination, CloneRequest(source))), "playbook_source_not_found");
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action == "playbook.cloned" && e.OrganizationId != null)));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task PublishedReadersCannotCreateCloneOrReadAdministrativeOrigin(OrganizationRole role)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var clone = await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source)));
        await Rejected(403, () => Playbooks(s => s.CloneAsync(seed.Platform, seed.Org.Id, CloneRequest(source))));
        await Rejected(403, () => Playbooks(s => s.OriginAsync(seed.Platform, seed.Org.Id, clone.Playbook.Id)));
        await Rejected(403, () => Playbooks(s => s.CloneAsync(seed.Admin, null, CloneRequest(source))));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("draft")]
    [InlineData("review")]
    [InlineData("archived")]
    [InlineData("superseded")]
    public async Task OnlyAnExplicitCurrentPublishedSourceCanBeCloned(string state)
    {
        var seed = await Seed(); var content = await DraftContent();
        var source = await Playbooks(s => s.CreateAsync(seed.Platform, null, new(content)));
        if (state != "draft") source = await SubmitPlaybook(seed.Platform, null, source);
        if (state is "archived" or "superseded") source = await DecidePlaybook(seed.Platform, null, source);
        if (state == "archived") source = await Playbooks(s => s.WithdrawAsync(seed.Platform, null, source.Playbook.Id, source.VersionId, new("Retirar", 3)));
        if (state == "superseded")
        {
            var next = await Playbooks(s => s.NewVersionAsync(seed.Platform, null, source.Playbook.Id, new(source.VersionId, "Reemplazar", 3)));
            next = await DecidePlaybook(seed.Platform, null, await SubmitPlaybook(seed.Platform, null, next));
            source = source with { Playbook = source.Playbook with { Revision = next.Playbook.Revision } };
        }
        await Rejected(409, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source))));
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.CountAsync()));
    }

    [Fact, Trait("Feature", "PlaybookCloning")]
    public async Task CloneCopiesPublishedSnapshotRatherThanNewDraftAndRejectsStaleSourceRevision()
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        var draft = await Playbooks(s => s.NewVersionAsync(seed.Platform, null, source.Playbook.Id, new(source.VersionId, "Nueva", 3)));
        draft = await Playbooks(s => s.UpdateAsync(seed.Platform, null, source.Playbook.Id,
            new(ContentForTest(draft) with { Title = "Secreto de borrador", Instructions = "No divulgar borrador" }, "Corregir", 4)));
        await Rejected(409, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source))), "stale_playbook_source_revision");
        source = source with { Playbook = source.Playbook with { Revision = draft.Playbook.Revision } };
        var clone = await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source)));
        Assert.Equal(source.Playbook.Title, clone.Playbook.Title); Assert.Equal(source.Instructions, clone.Instructions);
        Assert.DoesNotContain("Secreto", CloneContentJson(clone));
        var missing = CloneRequest(source) with { SourceVersionId = Guid.NewGuid() };
        await Rejected(409, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, missing)));
        await Rejected(404, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source) with { SourcePlaybookId = Guid.NewGuid() })));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    [InlineData("global_grant")]
    public async Task CloneRevalidatesDestinationAndActorAfterTakingSharedGate(string inactive)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        Guid? destination = inactive == "global_grant" ? null : seed.Org.Id;
        var actor = destination is null ? seed.Platform : seed.Admin;
        await InScope(async (_, db) =>
        {
            if (inactive == "membership") (await db.OrganizationMemberships.SingleAsync()).Status = MembershipStatus.Suspended;
            else if (inactive == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else if (inactive == "account") (await db.Users.SingleAsync(u => u.Id == actor)).IsActive = false;
            else if (inactive == "lockout") (await db.Users.SingleAsync(u => u.Id == actor)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            else (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Playbooks(s => s.CloneAsync(actor, destination, CloneRequest(source))));
        Assert.Equal(1, await InScope((_, db) => db.Playbooks.CountAsync()));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("category")]
    [InlineData("capability")]
    public async Task CloningRejectsInactiveCatalogReferencesWithoutDroppingRequirements(string inactive)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        await InScope(async (_, db) =>
        {
            if (inactive == "category") (await db.IncidentCategories.SingleAsync(c => c.Id == source.Playbook.CategoryId)).IsActive = false;
            else (await db.CapabilityCatalog.SingleAsync(c => c.Id == source.Requirements[0].CapabilityId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        await Rejected(409, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source))));
        Assert.Equal(2, await InScope((_, db) => db.PlaybookCapabilityRequirements.CountAsync()));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CloneRequiresNonemptyReason(string? reason)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        await Rejected(400, () => Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source) with { Reason = reason! })));
    }

    [Theory, Trait("Feature", "PlaybookCloning")]
    [InlineData("withdraw")]
    [InlineData("source_edit")]
    public async Task CloneVersusSourceMutationObservesOneConsistentRevision(string operation)
    {
        var seed = await Seed(); var source = await PublishedPlaybook(seed.Platform, null);
        PlaybookDraftItem? draft = null;
        if (operation == "source_edit")
        {
            draft = await Playbooks(s => s.NewVersionAsync(seed.Platform, null, source.Playbook.Id, new(source.VersionId, "Nueva", 3)));
            source = source with { Playbook = source.Playbook with { Revision = draft.Playbook.Revision } };
        }
        async Task<int> Clone()
        {
            try { await Playbooks(s => s.CloneAsync(seed.Admin, seed.Org.Id, CloneRequest(source))); return 201; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        async Task<int> Mutate()
        {
            if (operation == "withdraw") await Playbooks(s => s.WithdrawAsync(seed.Platform, null, source.Playbook.Id, source.VersionId, new("Retirada concurrente", source.Playbook.Revision)));
            else await Playbooks(s => s.UpdateAsync(seed.Platform, null, source.Playbook.Id,
                new(ContentForTest(draft!) with { Instructions = "Borrador concurrente secreto" }, "Editar", source.Playbook.Revision)));
            return 200;
        }
        var results = await Race(Clone, Mutate);
        Assert.Equal(200, results[1]); Assert.Contains(results[0], new[] { 201, 409 });
        var clones = (await Playbooks(s => s.ListAsync(seed.Admin, seed.Org.Id))).Items;
        Assert.Equal(results[0] == 201 ? 1 : 0, clones.Count);
        if (clones.Count == 1) Assert.Equal(source.Instructions, (await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, clones[0].Id))).Instructions);
    }
}
