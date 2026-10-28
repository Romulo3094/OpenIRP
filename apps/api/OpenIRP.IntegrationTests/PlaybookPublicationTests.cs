using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenIRP.Application.Playbooks;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<PlaybookDraftItem> SubmitPlaybook(Guid actor, Guid? org, PlaybookDraftItem item) =>
        Playbooks(s => s.SubmitAsync(actor, org, item.Playbook.Id, item.VersionId, new("Revisión privada", item.Playbook.Revision)));
    private Task<PlaybookDraftItem> DecidePlaybook(Guid actor, Guid? org, PlaybookDraftItem item,
        bool publish = true, bool self = true, string? justification = "Excepción evaluada") =>
        Playbooks(s => s.DecideAsync(actor, org, item.Playbook.Id, item.VersionId, item.ReviewRequestId!.Value,
            new(publish, self, justification, "Decisión privada", item.Playbook.Revision)));
    private async Task<PlaybookDraftItem> PublishedPlaybook(Guid actor, Guid? org)
    {
        var item = await Playbooks(async s => await s.CreateAsync(actor, org, new(await DraftContent())));
        return await DecidePlaybook(actor, org, await SubmitPlaybook(actor, org, item));
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybookPublicationFreezesContentAndWithdrawalLeavesNoImplicitFallback(bool global)
    {
        var seed = await Seed(); var actor = global ? seed.Platform : seed.Admin; Guid? org = global ? null : seed.Org.Id;
        var v1 = await PublishedPlaybook(actor, org);
        Assert.Equal("PUBLISHED", v1.Status); Assert.Equal(v1.VersionId, v1.Playbook.CurrentPublishedVersionId);
        var v2 = await Playbooks(s => s.NewVersionAsync(actor, org, v1.Playbook.Id, new(v1.VersionId, "Nueva versión", 3)));
        Assert.Equal(2, v2.VersionNumber); Assert.Equal(v1.VersionId, v2.SourceVersionId);
        Assert.DoesNotContain(v2.Steps[0].Id, v1.Steps.Select(s => s.Id));
        Assert.All(v2.Dependencies, d => { Assert.Contains(v2.Steps, s => s.Id == d.PredecessorId); Assert.Contains(v2.Steps, s => s.Id == d.SuccessorId); });
        var edited = ContentForTest(v2) with { Title = "Borrador confidencial", Instructions = "Instrucción nueva privada" };
        v2 = await Playbooks(async s => await s.UpdateAsync(actor, org, v1.Playbook.Id, new(edited, "Cambiar borrador", 4)));
        var old = await Playbooks(s => s.CurrentAsync(actor, org, v1.Playbook.Id));
        Assert.Equal(v1.Playbook.Title, old.Playbook.Title); Assert.Equal(v1.Instructions, old.Instructions);
        Assert.Equal(v1.VersionId, Assert.Single((await Playbooks(s => s.ListCurrentAsync(actor, org))).Items).CurrentPublishedVersionId);
        v2 = await DecidePlaybook(actor, org, await SubmitPlaybook(actor, org, v2));
        Assert.Equal(v2.VersionId, (await Playbooks(s => s.CurrentAsync(actor, org, v1.Playbook.Id))).VersionId);
        var archivedOld = await Playbooks(s => s.WithdrawAsync(actor, org, v1.Playbook.Id, v1.VersionId, new("Retirar anterior", 7)));
        Assert.Equal(v2.VersionId, archivedOld.Playbook.CurrentPublishedVersionId);
        var withdrawn = await Playbooks(s => s.WithdrawAsync(actor, org, v1.Playbook.Id, v2.VersionId, new("Retirar vigente sin sustituta", 8)));
        Assert.Null(withdrawn.Playbook.CurrentPublishedVersionId);
        await Rejected(404, () => Playbooks(s => s.CurrentAsync(actor, org, v1.Playbook.Id)));
        Assert.Empty((await Playbooks(s => s.ListCurrentAsync(actor, org))).Items);
        Assert.Equal(2, (await Playbooks(s => s.VersionsAsync(actor, org, v1.Playbook.Id))).Count);
        var history = await Playbooks(s => s.ReviewsAsync(actor, org, v1.Playbook.Id));
        Assert.Equal(6, history.Count); Assert.All(history, e => Assert.Equal(64, e.ContentHash.Length));
        Assert.Equal(history[0].ContentHash, history[1].ContentHash);
        Assert.Equal(history[2].ContentHash, history[3].ContentHash);
        Assert.NotEqual(history[1].ContentHash, history[3].ContentHash);
        Assert.DoesNotContain("privada", await InScope(async (_, db) => JsonSerializer.Serialize(await db.AuditEvents.Where(e => e.Action.StartsWith("playbook.")).ToListAsync())));
        Assert.Equal(v1.Instructions, (await Playbooks(s => s.VersionAsync(actor, org, v1.Playbook.Id, v1.VersionId))).Instructions);
    }

    private static PlaybookDraftContent ContentForTest(PlaybookDraftItem item)
    {
        var codes = item.Steps.ToDictionary(s => s.Id, s => s.Code);
        return new(item.Playbook.Title, item.Playbook.CategoryId, item.Objective, item.Instructions,
            item.Steps.Select(s => new PlaybookStepInput(s.Code, s.Position, s.Phase, s.Instructions, s.IsRequired, s.RequiresApproval, s.RequiresEvidence)).ToList(),
            item.Dependencies.Select(d => new PlaybookDependencyInput(codes[d.PredecessorId], codes[d.SuccessorId])).ToList(),
            item.Requirements.Select(r => new PlaybookRequirementInput(r.CapabilityId, r.StepId is null ? null : codes[r.StepId.Value], r.Level)).ToList());
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task RejectingAndResubmittingCannotReuseAnOldReviewRequest()
    {
        var seed = await Seed(); var item = await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(await DraftContent())));
        var pending = await SubmitPlaybook(seed.Admin, seed.Org.Id, item);
        await Rejected(409, () => Playbooks(async s => await s.UpdateAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(await DraftContent(), "Durante revisión", 2))));
        var rejected = await DecidePlaybook(seed.Admin, seed.Org.Id, pending, false, false, null);
        Assert.Equal("DRAFT", rejected.Status); Assert.Null(rejected.ReviewRequestId);
        var again = await SubmitPlaybook(seed.Admin, seed.Org.Id, rejected);
        Assert.NotEqual(pending.ReviewRequestId, again.ReviewRequestId);
        await Rejected(404, () => Playbooks(s => s.DecideAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, item.VersionId,
            pending.ReviewRequestId!.Value, new(true, true, "No reutilizable", "Repetir", 4))));
        var published = await DecidePlaybook(seed.Admin, seed.Org.Id, again);
        await Rejected(409, () => DecidePlaybook(seed.Admin, seed.Org.Id, published with { Playbook = published.Playbook with { Revision = 5 } }));
        Assert.Equal(4, (await Playbooks(s => s.ReviewsAsync(seed.Admin, seed.Org.Id, item.Playbook.Id))).Count);
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    public async Task PublicationRequiresExplicitJustifiedSelfApproval(bool self, string? justification)
    {
        var seed = await Seed(); var pending = await SubmitPlaybook(seed.Admin, seed.Org.Id,
            await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(await DraftContent()))));
        await Rejected(400, () => DecidePlaybook(seed.Admin, seed.Org.Id, pending, true, self, justification));
        Assert.Null((await Playbooks(s => s.GetAsync(seed.Admin, seed.Org.Id, pending.Playbook.Id))).Playbook.CurrentPublishedVersionId);
    }

    [Fact, Trait("Feature", "PlaybookPublication")]
    public async Task AnotherActiveOrganizationAdministratorCanPublishWithoutSelfApproval()
    {
        var seed = await Seed(); await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        var pending = await SubmitPlaybook(seed.Admin, seed.Org.Id,
            await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(await DraftContent()))));
        await Rejected(400, () => DecidePlaybook(seed.Platform, seed.Org.Id, pending, true, true, "No es autoaprobación"));
        var published = await DecidePlaybook(seed.Platform, seed.Org.Id, pending, true, false, null);
        Assert.Equal("PUBLISHED", published.Status);
        Assert.False((await Playbooks(s => s.ReviewsAsync(seed.Admin, seed.Org.Id, pending.Playbook.Id))).Last().SelfApproval);
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task PublishedReadersCannotSeeDraftsOrPublishAndGlobalGrantDoesNotBypassTenant(OrganizationRole role)
    {
        var seed = await Seed(); var published = await PublishedPlaybook(seed.Admin, seed.Org.Id);
        await Rejected(403, () => Playbooks(s => s.CurrentAsync(seed.Platform, seed.Org.Id, published.Playbook.Id)));
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var draft = await Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, published.Playbook.Id, new(published.VersionId, "Nueva", 3)));
        Assert.Equal(published.VersionId, (await Playbooks(s => s.CurrentAsync(seed.Platform, seed.Org.Id, published.Playbook.Id))).VersionId);
        await Rejected(403, () => Playbooks(s => s.VersionAsync(seed.Platform, seed.Org.Id, draft.Playbook.Id, draft.VersionId)));
        await Rejected(403, () => Playbooks(s => s.SubmitAsync(seed.Platform, seed.Org.Id, draft.Playbook.Id, draft.VersionId, new("No autorizado", 4))));
        var publicDraft = await Playbooks(async s => await s.CreateAsync(seed.Platform, null, new(await DraftContent())));
        Assert.Empty((await Playbooks(s => s.ListCurrentAsync(seed.Admin, null))).Items);
        await Rejected(404, () => Playbooks(s => s.CurrentAsync(seed.Admin, null, publicDraft.Playbook.Id)));
        var publicVersion = await DecidePlaybook(seed.Platform, null, await SubmitPlaybook(seed.Platform, null, publicDraft));
        Assert.Equal(publicVersion.VersionId, (await Playbooks(s => s.CurrentAsync(seed.Admin, null, publicDraft.Playbook.Id))).VersionId);
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("reviewer")]
    [InlineData("submitter")]
    [InlineData("organization")]
    [InlineData("lockout")]
    [InlineData("grant")]
    public async Task PublicationRevalidatesCurrentAccountMembershipAndGlobalGrant(string condition)
    {
        var seed = await Seed(); var global = condition == "grant"; Guid? org = global ? null : seed.Org.Id;
        var author = global ? seed.Platform : seed.Admin;
        if (!global) await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        var reviewer = seed.Platform;
        var pending = await SubmitPlaybook(author, org, await Playbooks(async s => await s.CreateAsync(author, org, new(await DraftContent()))));
        await InScope(async (_, db) =>
        {
            if (condition == "submitter") (await db.Users.SingleAsync(u => u.Id == author)).IsActive = false;
            else if (condition == "reviewer") (await db.OrganizationMemberships.SingleAsync(m => m.UserId == reviewer)).Status = MembershipStatus.Suspended;
            else if (condition == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else if (condition == "lockout") (await db.Users.SingleAsync(u => u.Id == reviewer)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            else (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => DecidePlaybook(reviewer, org, pending, true, global, global ? "Excepción" : null));
        Assert.Equal("IN_REVIEW", await InScope((_, db) => db.PlaybookVersions.Select(v => v.Status).SingleAsync()));
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("empty")]
    [InlineData("category")]
    [InlineData("capability")]
    [InlineData("cycle")]
    public async Task PublicationChecksContentAndCatalogAgain(string condition)
    {
        var seed = await Seed(); var content = await DraftContent();
        if (condition == "empty") content = content with { Steps = [], Dependencies = [], Requirements = [] };
        var item = await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(content)));
        if (condition == "empty") { await Rejected(409, () => SubmitPlaybook(seed.Admin, seed.Org.Id, item)); return; }
        if (condition == "cycle")
        {
            await InScope(async (_, db) => { db.PlaybookStepDependencies.Add(new() { VersionId = item.VersionId, PredecessorId = item.Steps[2].Id, SuccessorId = item.Steps[0].Id }); return await db.SaveChangesAsync(); });
            await Rejected(400, () => SubmitPlaybook(seed.Admin, seed.Org.Id, item)); return;
        }
        var pending = await SubmitPlaybook(seed.Admin, seed.Org.Id, item);
        await InScope(async (_, db) =>
        {
            if (condition == "category") (await db.IncidentCategories.SingleAsync(c => c.Id == content.CategoryId)).IsActive = false;
            else (await db.CapabilityCatalog.SingleAsync(c => c.Id == content.Requirements[0].CapabilityId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        await Rejected(409, () => DecidePlaybook(seed.Admin, seed.Org.Id, pending));
    }

    [Theory, Trait("Feature", "PlaybookPublication")]
    [InlineData("decisions")]
    [InlineData("versions")]
    public async Task ConcurrentPublicationOperationsCommitExactlyOneDecisionOrDraft(string operation)
    {
        var seed = await Seed(); var item = operation == "versions" ? await PublishedPlaybook(seed.Admin, seed.Org.Id)
            : await SubmitPlaybook(seed.Admin, seed.Org.Id, await Playbooks(async s => await s.CreateAsync(seed.Admin, seed.Org.Id, new(await DraftContent()))));
        async Task<int> Attempt(bool first)
        {
            try
            {
                if (operation == "versions") await Playbooks(s => s.NewVersionAsync(seed.Admin, seed.Org.Id, item.Playbook.Id, new(item.VersionId, "Nueva", item.Playbook.Revision)));
                else await DecidePlaybook(seed.Admin, seed.Org.Id, item, first, first, first ? "Excepción revisada" : null);
                return 200;
            }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Attempt(true), () => Attempt(false))).Order().ToArray());
        Assert.Equal(operation == "versions" ? 2 : 1, await InScope((_, db) => db.PlaybookVersions.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.PlaybookReviews.CountAsync()));
    }
}
