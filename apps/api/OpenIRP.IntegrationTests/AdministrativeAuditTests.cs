using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Audit;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    [Fact]
    public async Task AuditMigrationCanRoundTripOnEmptyDisposableDatabase()
    {
        await InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261022130822_InitialIdentityOrganizations");
            await using var check = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            check.CommandText = "SELECT to_regclass('audit.audit_events') IS NULL AND to_regprocedure('audit.reject_audit_mutation()') IS NULL";
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
            await db.Database.CloseConnectionAsync();
            await db.Database.MigrateAsync();
            Assert.Equal(0, await db.AuditEvents.CountAsync());
            return true;
        });
        await Seed();
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync()));
    }

    [Fact]
    public async Task RuntimeGrantScriptRefusesOwnerOrAdministrativeRole()
    {
        var connectionString = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role', current_user, false)", connection);
        await setting.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("dedicated NOINHERIT role", error.MessageText);
    }

    [Fact]
    public async Task UnavailableRejectionSinkDoesNotReplaceOriginalAccessDenial()
    {
        var seed = await Seed();
        await InScope(async (_, db) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION audit.reject_all_test_events() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected outage'; END; $body$;
                CREATE TRIGGER reject_all_test_events BEFORE INSERT ON audit.audit_events
                FOR EACH ROW EXECUTE FUNCTION audit.reject_all_test_events();
                """);
            return true;
        });
        await Rejected(403, () => Act(s => s.CreateOrganizationAsync(seed.Admin, new("Denied", "denied", "CL", Account("denied")))), "access_denied");
        Assert.Equal(1, await InScope((_, db) => db.Organizations.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync()));
    }
    private async Task<T> ReadAudit<T>(Func<IAuditReader, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAuditReader>());
    }

    [Fact]
    public async Task SuccessfulAdministrationHasMinimalTransactionalAudit()
    {
        var seed = await Seed();
        var events = await InScope((_, db) => db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync());
        Assert.Equal(2, events.Count);
        Assert.Equal("platform.bootstrap", events[0].Action);
        Assert.Null(events[0].OrganizationId);
        Assert.Equal("SYSTEM", events[0].ActorType);
        Assert.Equal(seed.Platform, events[0].ResourceId);
        Assert.Equal("organization.create", events[1].Action);
        Assert.Equal(seed.Org.Id, events[1].OrganizationId);
        Assert.Equal(seed.Platform, events[1].ActorUserId);
        Assert.All(events, e => { Assert.Equal("SUCCESS", e.Outcome); Assert.NotEqual(Guid.Empty, e.CorrelationId); });
        var json = JsonSerializer.Serialize(events);
        Assert.DoesNotContain(Password, json);
        Assert.DoesNotContain("example.test", json);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectedBusinessChangeRollsBackAndRecordsIndependentFailure()
    {
        var seed = await Seed();
        await Rejected(409, () => Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, seed.Membership,
            new(OrganizationRole.Viewer, MembershipStatus.Active, null))));
        var events = await InScope((_, db) => db.AuditEvents.AsNoTracking().ToListAsync());
        var failure = Assert.Single(events, e => e.Outcome == "FAILURE");
        Assert.Null(failure.OrganizationId);
        Assert.Null(failure.ResourceId);
        Assert.Contains("last_active_organization_administrator", failure.DetailsJson);
        Assert.DoesNotContain(events, e => e.Action == "membership.change" && e.Outcome == "SUCCESS");
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
    }

    [Fact]
    public async Task AuditInsertFailureRollsBackOrganizationAccountAndMembership()
    {
        var seed = await Seed();
        await InScope(async (_, db) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION audit.reject_test_success() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected audit sink failure'; END; $body$;
                CREATE TRIGGER reject_test_success BEFORE INSERT ON audit.audit_events FOR EACH ROW
                WHEN (NEW.outcome = 'SUCCESS') EXECUTE FUNCTION audit.reject_test_success();
                """);
            return true;
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => Act(s => s.CreateOrganizationAsync(seed.Platform,
            new("Should roll back", "rolled-back", "CL", Account("rolled-back")))));
        Assert.Equal(1, await InScope((_, db) => db.Organizations.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.Users.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.OrganizationMemberships.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "SUCCESS")));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "FAILURE")));
    }

    [Fact]
    public async Task AuditReadRespectsTenantBoundariesAndGlobalIsNotTenantBypass()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var own = await ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, seed.Org.Id));
        Assert.Single(own.Items);
        Assert.All(own.Items, e => Assert.Equal(seed.Org.Id, e.OrganizationId));
        await Rejected(403, () => ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, beta.Id)));
        await Rejected(403, () => ReadAudit(s => s.ReadOrganizationAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => ReadAudit(s => s.ReadGlobalAsync(seed.Admin)));
        var global = await ReadAudit(s => s.ReadGlobalAsync(seed.Platform));
        Assert.All(global.Items, e => Assert.Null(e.OrganizationId));
        Assert.Contains(global.Items, e => e.Outcome == "DENIED");
        Assert.DoesNotContain(global.Items, e => e.Action == "organization.create");
    }

    [Fact]
    public async Task AuditCursorPagesAreBoundedAndDoNotRepeatRows()
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Active, null)));
        var first = await ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, seed.Org.Id, limit: 2));
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextBefore);
        var second = await ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, seed.Org.Id, first.NextBefore, 2));
        Assert.Single(second.Items);
        Assert.Null(second.NextBefore);
        Assert.Empty(first.Items.Select(e => e.Id).Intersect(second.Items.Select(e => e.Id)));
        await Rejected(400, () => ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, seed.Org.Id, limit: 101)));
        await Rejected(400, () => ReadAudit(s => s.ReadOrganizationAsync(seed.Admin, seed.Org.Id, before: 0)));
    }

    [Theory]
    [InlineData("UPDATE audit.audit_events SET outcome = 'FAILURE'")]
    [InlineData("DELETE FROM audit.audit_events")]
    [InlineData("TRUNCATE TABLE audit.audit_events")]
    public async Task DatabaseRejectsAuditMutationEvenViaRawSql(string sql)
    {
        await Seed();
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) => await db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("42501", error.SqlState);
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync()));
    }

    [Fact]
    public async Task EfRejectsAuditUpdateAndDelete()
    {
        await Seed();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async (_, db) =>
        {
            var row = await db.AuditEvents.FirstAsync();
            db.Entry(row).Property(e => e.Outcome).CurrentValue = "FAILURE";
            return await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async (_, db) =>
        {
            db.AuditEvents.Remove(await db.AuditEvents.FirstAsync());
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task AuditHttpReturnsServerCorrelationAndRejectsTenantBypass()
    {
        var seed = await Seed();
        using var admin = Client();
        await Login(admin, "alpha");
        using var memberResponse = await admin.PostAsJsonAsync($"/api/organizations/{seed.Org.Id}/memberships", new AddMember(seed.Platform, OrganizationRole.Viewer));
        Assert.Equal(HttpStatusCode.Created, memberResponse.StatusCode);
        var correlation = Guid.Parse(Assert.Single(memberResponse.Headers.GetValues("X-Correlation-ID")));
        var created = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "membership.add"));
        Assert.Equal(correlation, created.CorrelationId);
        var page = await admin.GetFromJsonAsync<AuditPage>($"/api/organizations/{seed.Org.Id}/audit?limit=1");
        Assert.Single(page!.Items);
        Assert.Equal("membership.add", page.Items[0].Action);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/audit/global")).StatusCode);
        using var platform = Client();
        await Login(platform, "platform");
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/organizations/{seed.Org.Id}/audit")).StatusCode);
        var global = await platform.GetFromJsonAsync<AuditPage>("/api/audit/global");
        Assert.All(global!.Items, e => Assert.Null(e.OrganizationId));
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/audit/global")).StatusCode);
    }

    [Fact]
    public async Task RejectedCrossTenantWriteDoesNotCreateVictimTenantEvent()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        await Rejected(403, () => Act(s => s.ChangeMemberAsync(seed.Admin, beta.Id, Guid.NewGuid(), new(OrganizationRole.Viewer, MembershipStatus.Active, null))));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.OrganizationId == beta.Id)));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action == "membership.change" && e.Outcome == "DENIED" && e.OrganizationId == null)));
    }

    [Fact]
    public async Task RestrictedRuntimeCanWriteBusinessAndAppendAuditButCannotMutateHistory()
    {
        var role = "openirp_runtime_test_" + Guid.NewGuid().ToString("N");
        var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var ownerConnection = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var connection = new NpgsqlConnection(ownerConnection.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", connection))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role', @role, false)", connection))
            { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), connection))
                await grants.ExecuteNonQueryAsync();
            ownerConnection.Username = role;
            ownerConnection.Password = password;
            await using (var runtimeApp = new TestApplication(ownerConnection.ConnectionString))
            {
                using var scope = runtimeApp.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IIdentityAdministration>();
                var platform = await service.BootstrapAsync(Account("runtime-platform"));
                var organization = await service.CreateOrganizationAsync(platform, new("Runtime", "runtime", "CL", Account("runtime-admin")));
                var db = scope.ServiceProvider.GetRequiredService<OpenIrpDbContext>();
                Assert.Equal(2, await db.AuditEvents.CountAsync());
                var inventory = scope.ServiceProvider.GetRequiredService<OpenIRP.Application.Inventory.IInventoryService>();
                var administrator = await db.OrganizationMemberships.Where(m => m.OrganizationId == organization.Id).Select(m => m.UserId).SingleAsync();
                var capabilityId = await db.CapabilityCatalog.Where(c => c.Code == "BACKUPS").Select(c => c.Id).SingleAsync();
                await inventory.PutCapabilityAsync(administrator, organization.Id, capabilityId, new(CapabilityAvailability.Available, null, 0));
                await inventory.CreateAssetAsync(administrator, organization.Id, new("Runtime asset", AssetKind.Server, AssetCriticality.High, null));
                await inventory.CreateCatalogAsync(platform, OpenIRP.Application.Inventory.CatalogKind.Capabilities, new("RUNTIME_EXTENSION", "Runtime", "CUSTOM", null));
                Assert.Equal(5, await db.AuditEvents.CountAsync());
                var incidents = scope.ServiceProvider.GetRequiredService<OpenIRP.Application.Incidents.IIncidentService>();
                var category = await db.IncidentCategories.Select(c => c.Id).FirstAsync();
                var registered = await incidents.CreateAsync(administrator, organization.Id, new(category, "Runtime incident", null));
                await incidents.UpdateAsync(administrator, organization.Id, registered.Id, new(category, "Runtime corrected", null, "Correction", 1));
                Assert.Equal(2, (await incidents.EventsAsync(administrator, organization.Id, registered.Id)).Items.Count);
                Assert.Equal(7, await db.AuditEvents.CountAsync());
                var reported = await incidents.ReportAssetAsync(administrator, organization.Id, registered.Id,
                    new("Reported runtime host", AssetKind.Workstation, "Original observation", null, "Report", 2));
                var asset = await db.Assets.Select(a => a.Id).SingleAsync();
                await incidents.LinkAssetAsync(administrator, organization.Id, registered.Id, reported.Asset.Id, new(asset, "Identify", 3));
                Assert.Single((await incidents.AssetsAsync(administrator, organization.Id, registered.Id)).Items);
                Assert.Equal(9, await db.AuditEvents.CountAsync());
                await incidents.AssessCapabilityAsync(administrator, organization.Id, registered.Id, capabilityId,
                    new(CapabilityAvailability.Unavailable, "Runtime limitation", "Assessment", 4, 1, 1));
                await incidents.AssessCapabilityAsync(administrator, organization.Id, registered.Id, capabilityId,
                    new(null, null, "Inherit again", 5, 1, 1));
                var effective = (await incidents.CapabilitiesAsync(administrator, organization.Id, registered.Id)).Items.Single(c => c.CapabilityId == capabilityId);
                Assert.Equal(CapabilityAvailability.Available, effective.EffectiveAvailability);
                Assert.Null(effective.OverrideAvailability);
                Assert.Equal(11, await db.AuditEvents.CountAsync());
                var assessed = await incidents.AssessImpactAsync(administrator, organization.Id, registered.Id,
                    new(ImpactLevel.High, ImpactLevel.None, ImpactLevel.Low, "Runtime CIA", 6));
                Assert.True(assessed.IsImpactAssessmentComplete);
                await incidents.UpdateAsync(administrator, organization.Id, registered.Id,
                    new(category, "Still assessed", null, "Title correction", 7));
                var latest = (await incidents.EventsAsync(administrator, organization.Id, registered.Id)).Items.Last();
                Assert.Equal(ImpactLevel.High, latest.ConfidentialityImpact);
                Assert.Equal(13, await db.AuditEvents.CountAsync());
                foreach (var sql in new[] { "UPDATE app.incident_capability_assessments SET capability_id=capability_id", "DELETE FROM app.incident_capability_assessments", "TRUNCATE app.incident_capability_assessments", "ALTER TABLE app.incident_capability_assessments DISABLE TRIGGER ALL" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
                foreach (var sql in new[] { "UPDATE app.incident_assets SET reported_name='Tampered'", "DELETE FROM app.incident_assets", "TRUNCATE app.incident_assets", "ALTER TABLE app.incident_assets DISABLE TRIGGER ALL" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
                foreach (var sql in new[] { "UPDATE app.incident_events SET title='Tampered'", "DELETE FROM app.incident_events", "TRUNCATE app.incident_events", "ALTER TABLE app.incident_events DISABLE TRIGGER ALL" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
                foreach (var sql in new[] { "UPDATE audit.audit_events SET outcome='FAILURE'", "DELETE FROM audit.audit_events", "TRUNCATE audit.audit_events", "ALTER TABLE audit.audit_events DISABLE TRIGGER ALL" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
