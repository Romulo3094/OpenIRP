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
    private async Task<T> Incidents<T>(Func<IIncidentService, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IIncidentService>());
    }
    private async Task<Guid> IncidentCategoryId() => await InScope((_, db) => db.IncidentCategories.Where(c => c.Code == "PHISHING").Select(c => c.Id).SingleAsync());
    private async Task<IncidentCreate> NewIncident() => new(await IncidentCategoryId(), "Correo sospechoso privado", "Descripción confidencial");

    [Fact]
    public async Task IncidentRegistrationStoresSnapshotAndMinimalCorrelatedAuditAtomically()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        Assert.Equal("REGISTERED", item.Status);
        Assert.Equal(1, item.Revision);
        Assert.Null(item.AssignedMembershipId);
        var history = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id));
        var first = Assert.Single(history.Items);
        Assert.Equal(item.Title, first.Title);
        Assert.Equal(item.Description, first.Description);
        Assert.Equal(seed.Admin, first.ActorUserId);
        Assert.Equal("REGISTERED", first.Action);
        Assert.Null(first.Reason);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.registered"));
        Assert.Equal(first.CorrelationId, audit.CorrelationId);
        Assert.Equal(item.Id, audit.ResourceId);
        Assert.Equal(seed.Org.Id, audit.OrganizationId);
        Assert.DoesNotContain(request.Title, JsonSerializer.Serialize(audit));
        Assert.DoesNotContain(request.Description!, JsonSerializer.Serialize(audit));
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ActiveIncidentWritersCanRegisterEditAndAssign(OrganizationRole role)
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request));
        var updated = await Incidents(s => s.UpdateAsync(seed.Platform, seed.Org.Id, item.Id, new(item.CategoryId, "Título corregido", "Contexto nuevo", "Corrección justificada", 1)));
        Assert.Equal(2, updated.Revision);
        var assigned = await Incidents(s => s.AssignAsync(seed.Platform, seed.Org.Id, item.Id, new(member.Id, "Responsable disponible", 2)));
        Assert.Equal(3, assigned.Revision);
        Assert.Equal(member.Id, assigned.AssignedMembershipId);
        var unassigned = await Incidents(s => s.AssignAsync(seed.Platform, seed.Org.Id, item.Id, new(null, "Cambio de turno", 3)));
        Assert.Null(unassigned.AssignedMembershipId);
        var history = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, history.Items.Select(e => e.Revision));
        Assert.Equal(item.Title, history.Items[0].Title);
        Assert.Equal("Título corregido", history.Items[1].Title);
        Assert.Null(history.Items[1].AssignedMembershipId);
        Assert.Equal(member.Id, history.Items[2].AssignedMembershipId);
        Assert.Null(history.Items[3].AssignedMembershipId);
    }

    [Fact]
    public async Task ViewerCanReadButCannotChangeIncidents()
    {
        var seed = await Seed();
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        Assert.Single((await Incidents(s => s.ListAsync(seed.Platform, seed.Org.Id))).Items);
        await Incidents(s => s.GetAsync(seed.Platform, seed.Org.Id, item.Id));
        await Incidents(s => s.EventsAsync(seed.Platform, seed.Org.Id, item.Id));
        await Rejected(403, () => Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request)));
        await Rejected(403, () => Incidents(s => s.UpdateAsync(seed.Platform, seed.Org.Id, item.Id, new(item.CategoryId, "Denied", null, "Denied", 1))));
        await Rejected(403, () => Incidents(s => s.AssignAsync(seed.Platform, seed.Org.Id, item.Id, new(null, "Denied", 1))));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task IncidentsAndEventsRemainTenantScopedWithoutPlatformBypass()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Rejected(403, () => Incidents(s => s.ListAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => Incidents(s => s.EventsAsync(seed.Platform, seed.Org.Id, item.Id)));
        await Rejected(403, () => Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request)));
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        Assert.Empty((await Incidents(s => s.ListAsync(betaAdmin, beta.Id))).Items);
        await Rejected(404, () => Incidents(s => s.GetAsync(betaAdmin, beta.Id, item.Id)));
        await Rejected(404, () => Incidents(s => s.EventsAsync(betaAdmin, beta.Id, item.Id)));
        await Rejected(404, () => Incidents(s => s.UpdateAsync(betaAdmin, beta.Id, item.Id, new(item.CategoryId, "Denied", null, "Denied", 1))));
        await Rejected(403, () => Incidents(s => s.GetAsync(betaAdmin, seed.Org.Id, item.Id)));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action.StartsWith("incident.") && e.OrganizationId == seed.Org.Id)));
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("suspended")]
    [InlineData("revoked")]
    [InlineData("inactive-account")]
    [InlineData("locked-account")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public async Task InvalidIncidentAssigneeIsRejectedWithoutNewRevision(string condition)
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, condition == "viewer" ? OrganizationRole.Viewer : OrganizationRole.Analyst)));
        var target = member.Id;
        if (condition is "suspended" or "revoked")
            await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst,
                condition == "suspended" ? MembershipStatus.Suspended : MembershipStatus.Revoked, null)));
        if (condition == "inactive-account")
            await InScope(async (_, db) => { (await db.Users.SingleAsync(u => u.Id == seed.Platform)).IsActive = false; return await db.SaveChangesAsync(); });
        if (condition == "locked-account")
            await InScope(async (_, db) => { (await db.Users.SingleAsync(u => u.Id == seed.Platform)).LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15); return await db.SaveChangesAsync(); });
        if (condition == "foreign")
        {
            var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
            target = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.Id).SingleAsync());
        }
        if (condition == "missing") target = Guid.NewGuid();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Rejected(400, () => Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, item.Id, new(target, "No válido", 1))), "invalid_incident_assignee");
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Id))).Revision);
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task CategoryDeactivationPreservesExistingIncidentButPreventsNewUse()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Inventory(s => s.UpdateCatalogAsync(seed.Platform, CatalogKind.IncidentCategories, item.CategoryId, new("Retirada", "CUSTOM", null, false, 1)));
        await Rejected(400, () => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request)));
        await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(item.CategoryId, "Corrección", null, "Conservar referencia histórica", 1)));
        var other = await Inventory(s => s.CreateCatalogAsync(seed.Platform, CatalogKind.IncidentCategories, new("OTHER_TEST", "Otra", "CUSTOM", null)));
        await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(other.Id, "Corrección", null, "Reclasificación", 2)));
        await Rejected(400, () => Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(item.CategoryId, "Denied", null, "Intento sobre inactiva", 3))));
        Assert.Equal(item.CategoryId, (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id))).Items[0].CategoryId);
    }

    [Fact]
    public async Task IncidentInputRequiresValidLengthsCategoryReasonAndRevision()
    {
        var seed = await Seed();
        var request = await NewIncident();
        await Rejected(400, () => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request with { Title = " " })));
        await Rejected(400, () => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request with { Title = new string('x', 201) })));
        await Rejected(400, () => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request with { Description = new string('x', 4001) })));
        await Rejected(400, () => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request with { CategoryId = Guid.NewGuid() })));
        Assert.Equal(0, await InScope((_, db) => db.Incidents.CountAsync()));
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Rejected(400, () => Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(item.CategoryId, item.Title, null, " ", 1))));
        await Rejected(400, () => Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, item.Id, new(null, new string('x', 2001), 1))));
        await Rejected(409, () => Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(item.CategoryId, item.Title, null, "Obsoleto", 0))));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task IncidentPagesAndEventCursorsAreBoundedAndOrdered()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        var page = await Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id, limit: 1));
        var next = await Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id, skip: page.NextSkip!.Value, limit: 1));
        Assert.NotEqual(Assert.Single(page.Items).Id, Assert.Single(next.Items).Id);
        Assert.Null(next.NextSkip);
        await Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, item.Id, new(seed.Membership, "Asignar", 1)));
        var first = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id, limit: 1));
        Assert.Equal(1, first.NextAfterRevision);
        var second = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id, first.NextAfterRevision!.Value, 1));
        Assert.Equal(2, Assert.Single(second.Items).Revision);
        Assert.Null(second.NextAfterRevision);
        await Rejected(400, () => Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, item.Id, -1)));
        await Rejected(400, () => Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id, limit: 101)));
    }

    [Fact]
    public async Task ConcurrentIncidentEditsCommitExactlyOneNewSnapshot()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        async Task<int> Attempt(bool assignment)
        {
            try
            {
                if (assignment) await Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, item.Id, new(seed.Membership, "Asignar", 1)));
                else await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item.Id, new(item.CategoryId, "Edición concurrente", null, "Corregir", 1)));
                return 200;
            }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Attempt(true), () => Attempt(false))).Order().ToArray());
        Assert.Equal(2, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(2, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, item.Id))).Revision);
    }

    [Fact]
    public async Task IncidentWriterReauthorizesAgainstConcurrentMembershipRevocation()
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Analyst)));
        var request = await NewIncident();
        async Task<int> Write()
        {
            try { await Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request)); return 200; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        async Task<int> Revoke()
        {
            await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Revoked, null)));
            return 200;
        }
        var results = await Race(Write, Revoke);
        Assert.Contains(results[0], new[] { 200, 403 });
        Assert.Equal(200, results[1]);
        Assert.Equal(results[0] == 200 ? 1 : 0, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        await Rejected(403, () => Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncidentAuditFailureRollsBackBusinessAndTimeline(bool editing)
    {
        var seed = await Seed();
        var request = await NewIncident();
        var item = editing ? await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request)) : null;
        await InScope(async (_, db) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION audit.reject_incident_test() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected outage'; END; $body$;
                CREATE TRIGGER reject_incident_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
                WHEN (NEW.outcome='SUCCESS' AND NEW.action LIKE 'incident.%') EXECUTE FUNCTION audit.reject_incident_test();
                """);
            return true;
        });
        if (editing)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, item!.Id, new(item.CategoryId, "Must roll back", null, "Prueba", 1))));
            Assert.Equal(request.Title, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, item!.Id))).Title);
        }
        else await Assert.ThrowsAsync<DbUpdateException>(() => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request)));
        Assert.Equal(editing ? 1 : 0, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(editing ? 1 : 0, await InScope((_, db) => db.Incidents.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "FAILURE")));
    }

    [Fact]
    public async Task IncidentEventFailureRollsBackIncidentAndAudit()
    {
        var seed = await Seed();
        var request = await NewIncident();
        await InScope(async (_, db) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION app.reject_incident_insert_test() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected timeline outage'; END; $body$;
                CREATE TRIGGER reject_incident_insert_test BEFORE INSERT ON app.incident_events FOR EACH ROW EXECUTE FUNCTION app.reject_incident_insert_test();
                """);
            return true;
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request)));
        Assert.Equal(0, await InScope((_, db) => db.Incidents.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action.StartsWith("incident.") && e.Outcome == "SUCCESS")));
    }

    [Fact]
    public async Task IncidentHistoryCannotBeMutatedThroughEfOrSql()
    {
        var seed = await Seed();
        var request = await NewIncident();
        await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async (_, db) =>
        { (await db.IncidentEvents.SingleAsync()).Title = "Tampered"; return await db.SaveChangesAsync(); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async (_, db) =>
        { db.IncidentEvents.Remove(await db.IncidentEvents.SingleAsync()); return await db.SaveChangesAsync(); }));
        foreach (var sql in new[] { "UPDATE app.incident_events SET title='Tampered'", "DELETE FROM app.incident_events", "TRUNCATE app.incident_events" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
            Assert.Equal("42501", error.SqlState);
        }
        Assert.Equal(request.Title, await InScope((_, db) => db.IncidentEvents.Select(e => e.Title).SingleAsync()));
    }

    [Fact]
    public async Task DatabaseCompositeKeysRejectForeignIncidentAndAssigneeReferences()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaMember = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == beta.Id));
        var request = await NewIncident();
        var item = await Incidents(s => s.CreateAsync(seed.Admin, seed.Org.Id, request));
        var assignment = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        { (await db.Incidents.SingleAsync()).AssignedMembershipId = betaMember.Id; return await db.SaveChangesAsync(); }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(assignment.InnerException).SqlState);
        var history = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.IncidentEvents.Add(new() { OrganizationId = beta.Id, IncidentId = item.Id, Revision = 1,
                ActorUserId = betaMember.UserId, Action = "REGISTERED", CategoryId = item.CategoryId, Title = "Wrong tenant" });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(history.InnerException).SqlState);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("membership")]
    [InlineData("account")]
    public async Task IncidentReadAndWriteRejectInactiveContext(string condition)
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Analyst)));
        var request = await NewIncident();
        if (condition == "membership") await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Suspended, null)));
        else await InScope(async (_, db) =>
        {
            if (condition == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else (await db.Users.SingleAsync(u => u.Id == seed.Platform)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Incidents(s => s.ListAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => Incidents(s => s.CreateAsync(seed.Platform, seed.Org.Id, request)));
    }

    [Fact]
    public async Task HttpIncidentsRequireCsrfRevisionAndExplicitAssignment()
    {
        var seed = await Seed();
        var request = await NewIncident();
        var route = $"/api/organizations/{seed.Org.Id}/incidents";
        using var anon = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(route)).StatusCode);
        using var admin = Client();
        await Login(admin, "alpha");
        var response = await admin.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<IncidentItem>())!;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{route}/{item.Id}/assignment", new { reason = "Missing target", expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{route}/{item.Id}/assignment", new { assignedMembershipId = seed.Membership, reason = "Missing revision" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{route}/{item.Id}/assignment", new IncidentAssignment(seed.Membership, "Asignación HTTP", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"{route}/{item.Id}", new IncidentUpdate(item.CategoryId, "Obsoleto", null, "Intento", 1))).StatusCode);
        var events = await admin.GetFromJsonAsync<IncidentEventPage>($"{route}/{item.Id}/events");
        Assert.Equal(2, events!.Items.Count);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route, request)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync($"{route}/{item.Id}")).StatusCode);
    }
}
