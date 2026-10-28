using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<IncidentItem> CloseWithContext(Guid actor, Guid org, IncidentItem i, string? token) =>
        Incidents(s => s.TransitionAsync(actor, org, i.Id, new(IncidentState.Closed, null, "Cierre con contexto revisado", i.Revision, token)));

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task ClosureContextIsStableAcrossReadersAndDoesNotWriteOrExposeContent()
    {
        var f = await Seed(); await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, OrganizationRole.Viewer)));
        var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var before = await InScope((_, db) => db.AuditEvents.CountAsync());
        var a = await ClosureRead(f.Admin, f.Org.Id, i.Id); var b = await ClosureRead(f.Platform, f.Org.Id, i.Id);
        Assert.Matches("^closure-v1:[0-9a-f]{64}$", a.ClosureContextToken); Assert.Equal(a.ClosureContextToken, b.ClosureContextToken);
        Assert.Equal(before, await InScope((_, db) => db.AuditEvents.CountAsync()));
        Assert.True(a.ResidualRiskEvaluated); Assert.DoesNotContain(i.Resolution!, a.ClosureContextToken);
        await Rejected(403, () => CloseWithContext(f.Platform, f.Org.Id, i, a.ClosureContextToken));
        var closed = await CloseWithContext(f.Admin, f.Org.Id, i, a.ClosureContextToken);
        Assert.Equal("CLOSED", closed.Status);
        var ev = (await Incidents(s => s.EventsAsync(f.Admin, f.Org.Id, i.Id))).Items.Last();
        Assert.Equal(a.ClosureContextToken, ev.Details.GetProperty("closureContextToken").GetString());
        Assert.Equal("CLOSED", ev.Details.GetProperty("workflow").GetProperty("to").GetString());
    }

    [Theory, Trait("Feature", "ClosureContext")]
    [InlineData("")] [InlineData(" ")] [InlineData("closure-v2:0000")] [InlineData("closure-v1:abcd")]
    [InlineData("closure-v1:ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public async Task MalformedClosureContextIsRejectedWithoutChangingIncident(string token)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        await Rejected(400, () => CloseWithContext(f.Admin, f.Org.Id, i, token), "invalid_closure_context");
        var current = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)); Assert.Equal(i, current);
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task ClosureContextCannotBeSilentlyIgnoredOnAnotherStateTransition()
    {
        var f = await ExecutionSeed(); var r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        await Rejected(400, () => Incidents(s => s.TransitionAsync(f.Admin, f.Org, f.Incident.Id,
            new(IncidentState.UnderEvaluation, null, "Otra transicion", r.IncidentRevision, r.ClosureContextToken))), "unexpected_closure_context");
    }

    [Theory, Trait("Feature", "ClosureContext")]
    [InlineData("close")] [InlineData("request")] [InlineData("approve")] [InlineData("reject")]
    public async Task EvidenceWithdrawalInvalidatesClosurePreconditionWithoutChangingIncidentRevision(string phase)
    {
        var f = await ExecutionSeed(); var evidence = await AddEvidence(f);
        var parent = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id));
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, phase != "close", parent);
        if (phase is "approve" or "reject") i = await AskClosure(f.Admin, f.Org, i);
        var context = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken;
        await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, i.Id, evidence.Evidence.Id, new(1, "Retirar soporte")));
        var count = await InScope((_, db) => db.IncidentEvents.CountAsync());
        Task<IncidentItem> Attempt(string token) => phase switch {
            "close" => CloseWithContext(f.Admin, f.Org, i, token),
            "request" => Incidents(s => s.RequestClosureAsync(f.Admin, f.Org, i.Id, new("Solicitar", i.Revision, token))),
            _ => Incidents(s => s.DecideClosureAsync(f.Admin, f.Org, i.Id, i.ClosureReview!.Id,
                new(phase == "approve", phase == "approve", phase == "approve" ? "Excepcion documentada" : null, "Decidir", i.Revision, token)))
        };
        await Rejected(409, () => Attempt(context), "stale_closure_context");
        Assert.Equal(i, await Incidents(s => s.GetAsync(f.Admin, f.Org, i.Id)));
        Assert.Equal(count, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        var fresh = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken; Assert.NotEqual(context, fresh);
        if (phase != "reject")
        {
            await Rejected(409, () => Attempt(fresh), "current_closure_risk_acceptance_required");
            i = await AcceptRiskForTest(f.Admin, f.Org, i);
            if (phase == "approve") i = await AskClosure(f.Admin, f.Org, i);
            fresh = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken;
        }
        var result = await Attempt(fresh);
        Assert.Equal(i.Revision + 1, result.Revision);
        var last = (await Incidents(s => s.EventsAsync(f.Admin, f.Org, i.Id))).Items.Last();
        Assert.Equal(fresh, last.Details.GetProperty("closureContextToken").GetString());
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task NewSourceSecurityAlertChangesTerminalExecutionClosureContextWithoutRewritingHistory()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        e = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Respuesta manual")));
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        var old = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken;
        await HoldVersion(f);
        Assert.Equal(i.Revision, (await Incidents(s => s.GetAsync(f.Admin, f.Org, i.Id))).Revision);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org, i, old), "stale_closure_context");
        Assert.NotEqual(old, (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken);
        Assert.Equal("CANCELLED", (await Executions(s => s.GetAsync(f.Admin, f.Org, i.Id, e.Id))).Status);
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task FileRecoveryInvalidatesContextWithoutClaimingPhysicalIntegrity()
    {
        var f = await ExecutionSeed();
        await WithFiles(async (runtime, root) => {
            var upload = await UploadFile(runtime, f);
            var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
            var before = await ClosureRead(f.Admin, f.Org, i.Id); Assert.Equal(1, before.ActiveUnavailableFiles);
            await Files(runtime, s => s.RecoverFileAsync(f.Admin, f.Org, i.Id, upload.File.EvidenceId, new(2, "Recuperar")));
            var after = await ClosureRead(f.Admin, f.Org, i.Id); Assert.Equal(i.Revision, after.IncidentRevision);
            Assert.Equal(0, after.ActiveUnavailableFiles); Assert.False(after.FileIntegrityVerified);
            await Rejected(409, () => CloseWithContext(f.Admin, f.Org, i, before.ClosureContextToken), "stale_closure_context");
        }, failure: "publish_once");
    }

    [Theory, Trait("Feature", "ClosureContext")]
    [InlineData("profile")] [InlineData("catalog")] [InlineData("membership")] [InlineData("category")]
    public async Task ExternalContextChangesInvalidateTokenEvenWhenIncidentRevisionIsUnchanged(string change)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        e = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Respuesta manual")));
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        var old = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken;
        var capability = f.Playbook.Requirements[0].CapabilityId;
        if (change == "profile") await Inventory(s => s.PutCapabilityAsync(f.Admin, f.Org, capability, new(CapabilityAvailability.Available, "Declaracion actualizada", 0)));
        if (change == "catalog") await Inventory(s => s.UpdateCatalogAsync(f.Platform, CatalogKind.Capabilities, capability, new("Actualizado", "CUSTOM", null, false, 1)));
        if (change == "category") await Inventory(s => s.UpdateCatalogAsync(f.Platform, CatalogKind.IncidentCategories, i.CategoryId, new("Actualizada", "CUSTOM", null, true, 1)));
        if (change == "membership") await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Viewer)));
        Assert.Equal(i.Revision, (await Incidents(s => s.GetAsync(f.Admin, f.Org, i.Id))).Revision);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org, i, old), "stale_closure_context");
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task OtherIncidentsOrganizationsAndAuditRowsDoNotInvalidateContext()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var old = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        await RegisterIncident(f.Admin, f.Org.Id);
        await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        await Rejected(403, () => ClosureRead(f.Platform, f.Org.Id, i.Id));
        Assert.Equal(old, (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken);
        Assert.Equal("CLOSED", (await CloseWithContext(f.Admin, f.Org.Id, i, old)).Status);
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task IncidentRevisionsAndDifferentIncidentsCannotReuseClosureContext()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var old = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        var other = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, other, old), "stale_closure_context");
        i = await Incidents(s => s.UpdateAsync(f.Admin, f.Org.Id, i.Id, new(i.CategoryId, "Correccion", null, "Motivo", i.Revision)));
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, i, old), "stale_closure_context");
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task ConcurrentSnapshotKeepsContextAndReportOnTheSameRevision()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        var old = (await ClosureRead(f.Admin, f.Org, f.Incident.Id)).ClosureContextToken;
        var interceptor = new ClosureConcurrentChange(async () => {
            await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Cancelacion concurrente")));
        });
        var connection = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()!));
        await using var runtime = new ReadinessApplication(connection, interceptor); using var scope = runtime.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<IIncidentService>().ClosureReadinessAsync(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(old, r.ClosureContextToken); Assert.Equal(e.IncidentRevision, r.IncidentRevision); Assert.Equal(e.Id, r.ActiveExecutionId);
        Assert.NotEqual(old, (await ClosureRead(f.Admin, f.Org, f.Incident.Id)).ClosureContextToken);
    }

    [Theory, Trait("Feature", "ClosureContext")]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentWithdrawalAndClosureAreSerializedUnderTheSameGate(bool closeFirst)
    {
        var f = await ExecutionSeed(); var row = await AddEvidence(f);
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        var token = (await ClosureRead(f.Admin, f.Org, i.Id)).ClosureContextToken;
        async Task<int> Close() { try { await CloseWithContext(f.Admin, f.Org, i, token); return 200; }
            catch (IdentityOperationException ex) { Assert.Equal("stale_closure_context", ex.Code); return ex.StatusCode; } }
        async Task<int> Withdraw() { await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, i.Id, row.Evidence.Id, new(1, "Retiro concurrente"))); return 200; }
        var results = await Race(closeFirst ? Close : Withdraw, closeFirst ? Withdraw : Close);
        var closeStatus = results[closeFirst ? 0 : 1]; Assert.Equal(200, results[closeFirst ? 1 : 0]);
        var latest = await Incidents(s => s.GetAsync(f.Admin, f.Org, i.Id));
        Assert.Equal(closeStatus == 200 ? "CLOSED" : "RESOLVED", latest.Status);
        Assert.Contains(closeStatus, new[] { 200, 409 });
    }

    [Fact, Trait("Feature", "ClosureContext")]
    public async Task ContextHttpRequiresAuthenticationAndPreservesCsrfOnWrites()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var root = $"/api/organizations/{f.Org.Id}/incidents/{i.Id}";
        using var client = Client(); await Login(client, "alpha");
        var report = await client.GetFromJsonAsync<JsonElement>(root + "/closure-readiness");
        var token = report.GetProperty("closureContextToken").GetString();
        var request = new TransitionIncident(IncidentState.Closed, null, "Cierre", i.Revision, token);
        var csrf = client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single(); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(root + "/state", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(root + "/state", request)).StatusCode);
    }
}
