using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;
using Xunit;

namespace OpenIRP.IntegrationTests;

// Each case owns a newly created database. No migration, truncate, or delete touches a supplied database.
public sealed partial class IdentityAdministrationTests : IAsyncLifetime
{
    private string adminConnection = "";
    private string database = "";
    private TestApplication app = null!;
    private const string Password = "Only-For-Tests!2026";
    private static NewAccount Account(string name) => new($"{name}@example.test", Password);

    private sealed class TestApplication(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:OpenIRP"] = connection }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("OPENIRP_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set OPENIRP_TEST_POSTGRES to a disposable local PostgreSQL cluster; tests never skip silently.");
        var cs = new NpgsqlConnectionStringBuilder(adminConnection);
        if (cs.Host is not ("127.0.0.1" or "localhost" or "::1")) throw new InvalidOperationException("Only loopback test servers allowed.");
        database = "openirp_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(adminConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await command.ExecuteNonQueryAsync();
        }
        cs.Database = database;
        app = new TestApplication(cs.ConnectionString);
        await InScope(async (_, db) =>
        {
            Assert.Equal(database, db.Database.GetDbConnection().Database);
            await db.Database.MigrateAsync();
            return true;
        });
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        if (database.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> InScope<T>(Func<IIdentityAdministration, OpenIrpDbContext, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IIdentityAdministration>(),
            scope.ServiceProvider.GetRequiredService<OpenIrpDbContext>());
    }
    private Task<T> Act<T>(Func<IIdentityAdministration, Task<T>> action) => InScope((service, _) => action(service));
    private async Task<(Guid Platform, OrganizationView Org, Guid Admin, Guid Membership)> Seed(string name = "alpha")
    {
        var platform = await Act(s => s.BootstrapAsync(Account("platform")));
        var org = await Act(s => s.CreateOrganizationAsync(platform, new(name, name, "CL", Account(name))));
        var membership = await InScope((_, db) => db.OrganizationMemberships.AsNoTracking().SingleAsync(m => m.OrganizationId == org.Id));
        return (platform, org, membership.UserId, membership.Id);
    }
    private static async Task Rejected(int status, Func<Task> action, string? code = null)
    {
        var error = await Assert.ThrowsAsync<IdentityOperationException>(action);
        Assert.Equal(status, error.StatusCode);
        if (code is not null) Assert.Equal(code, error.Code);
    }
    private async Task<int> ActiveAdmins(Guid org) => await InScope((_, db) => db.OrganizationMemberships.CountAsync(m =>
        m.OrganizationId == org && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin
        && db.Users.Any(u => u.Id == m.UserId && u.IsActive)));

    // Hold the same database gate until BOTH writers are queued: verify real overlap,
    // rather than merely running two operations that might finish sequentially.
    private async Task<int[]> Race(Func<Task<int>> first, Func<Task<int>> second)
    {
        var connectionString = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1869636978, 26)", connection, tx);
        await gate.ExecuteNonQueryAsync();
        var a = first();
        var b = second();
        var waiters = 0;
        try
        {
            for (var i = 0; i < 60; i++)
            {
                await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND classid=1869636978 AND objid=26 AND NOT granted AND database=(SELECT oid FROM pg_database WHERE datname=current_database())", connection, tx);
                waiters = Convert.ToInt32(await count.ExecuteScalarAsync());
                if (waiters >= 2) break;
                await Task.Delay(50);
            }
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(a, b);
        Assert.Equal(2, waiters);
        return results;
    }
    [Fact]
    public async Task BootstrapIsOneShotEvenAfterRevocation()
    {
        var id = await Act(s => s.BootstrapAsync(Account("platform")));
        await InScope(async (_, db) => { (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return true; });
        await Rejected(409, () => Act(s => s.BootstrapAsync(Account("other"))), "bootstrap_already_completed");
        Assert.Equal(1, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentBootstrapCreatesExactlyOneAdministrator()
    {
        async Task<int> Attempt(string name) { try { await Act(s => s.BootstrapAsync(Account(name))); return 201; } catch (IdentityOperationException e) { return e.StatusCode; } }
        var statuses = await Race(() => Attempt("one"), () => Attempt("two"));
        Assert.Equal(new[] { 201, 409 }, statuses.Order().ToArray());
        Assert.Equal(1, await InScope((_, db) => db.PlatformAdministrators.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Fact]
    public async Task BootstrapNeverPromotesExistingAccount()
    {
        await InScope(async (_, db) => { db.Users.Add(new() { Id = Guid.NewGuid(), Email = "existing@example.test", NormalizedEmail = "EXISTING@EXAMPLE.TEST" }); await db.SaveChangesAsync(); return true; });
        await Rejected(409, () => Act(s => s.BootstrapAsync(Account("existing"))), "account_already_exists");
        Assert.Equal(0, await InScope((_, db) => db.PlatformAdministrators.CountAsync()));
    }

    [Fact]
    public async Task OrganizationAndFirstAdminCommitTogether()
    {
        var seed = await Seed();
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
        Assert.Empty(await Act(s => s.ListOrganizationsAsync(seed.Platform)));
        Assert.Single(await Act(s => s.ListOrganizationsAsync(seed.Admin)));
    }

    [Fact]
    public async Task InvalidPasswordRollsBackAlreadyInsertedOrganization()
    {
        var platform = await Act(s => s.BootstrapAsync(Account("platform")));
        await Rejected(400, () => Act(s => s.CreateOrganizationAsync(platform, new("Bad", "bad", "CL", new("bad@example.test", "weak")))));
        Assert.Equal(0, await InScope((_, db) => db.Organizations.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Fact]
    public async Task DuplicateSlugDoesNotLeaveOrphanAccount()
    {
        var seed = await Seed();
        await Rejected(409, () => Act(s => s.CreateOrganizationAsync(seed.Platform, new("Duplicate", "alpha", "CL", Account("unused")))));
        Assert.Equal(2, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentDuplicateSlugsCommitOnlyOneOrganization()
    {
        var platform = await Act(s => s.BootstrapAsync(Account("platform")));
        async Task<int> Attempt(string email) { try { await Act(s => s.CreateOrganizationAsync(platform, new("Concurrent", "same", "CL", Account(email)))); return 201; } catch (IdentityOperationException e) { return e.StatusCode; } }
        Assert.Equal(new[] { 201, 409 }, (await Race(() => Attempt("a"), () => Attempt("b"))).Order().ToArray());
        Assert.Equal(1, await InScope((_, db) => db.Organizations.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer, MembershipStatus.Active)]
    [InlineData(OrganizationRole.OrgAdmin, MembershipStatus.Suspended)]
    [InlineData(OrganizationRole.OrgAdmin, MembershipStatus.Revoked)]
    public async Task LastAdministratorCannotBeRemoved(OrganizationRole role, MembershipStatus status)
    {
        var seed = await Seed();
        await Rejected(409, () => Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, seed.Membership, new(role, status, null))), "last_active_organization_administrator");
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
    }

    [Fact]
    public async Task DeactivationCannotRemoveLastAdministrator()
    {
        var seed = await Seed();
        await Rejected(409, () => Act(async s => { await s.SetUserActiveAsync(seed.Platform, seed.Admin, false); return true; }));
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
    }

    [Fact]
    public async Task CrossTenantIdsAndPlatformPrivilegeDoNotGrantTenantAccess()
    {
        var seed = await Seed();
        var other = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        await Rejected(403, () => Act(s => s.ListMembersAsync(seed.Admin, other.Id)));
        await Rejected(403, () => Act(s => s.ListMembersAsync(seed.Platform, seed.Org.Id)));
        var betaMember = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == other.Id));
        await Rejected(404, () => Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, betaMember.Id, new(OrganizationRole.Viewer, MembershipStatus.Active, null))));
        await Rejected(403, () => Act(s => s.CreateOrganizationAsync(seed.Admin, new("Denied", "denied", "CL", Account("denied")))));
    }

    [Fact]
    public async Task ConcurrentRevocationsLeaveAnAdministrator()
    {
        var seed = await Seed();
        var second = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        async Task<int> Attempt(Guid actor, Guid target)
        { try { await Act(s => s.ChangeMemberAsync(actor, seed.Org.Id, target, new(OrganizationRole.OrgAdmin, MembershipStatus.Revoked, null))); return 200; } catch (IdentityOperationException e) { return e.StatusCode; } }
        var result = await Race(() => Attempt(seed.Admin, seed.Membership), () => Attempt(seed.Platform, second.Id));
        Assert.Single(result, status => status == 200);
        Assert.Contains(409, result);
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
    }

    [Fact]
    public async Task DeactivationRacingWithDemotionCannotRemoveBothAdmins()
    {
        var seed = await Seed();
        var other = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var beta = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == other.Id));
        var second = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(beta.UserId, OrganizationRole.OrgAdmin)));
        async Task<int> Deactivate() { try { await Act(async s => { await s.SetUserActiveAsync(seed.Platform, seed.Admin, false); return true; }); return 200; } catch (IdentityOperationException e) { return e.StatusCode; } }
        async Task<int> Demote() { try { await Act(s => s.ChangeMemberAsync(beta.UserId, seed.Org.Id, second.Id, new(OrganizationRole.Viewer, MembershipStatus.Active, null))); return 200; } catch (IdentityOperationException e) { return e.StatusCode; } }
        var result = await Race(Deactivate, Demote);
        Assert.Single(result, status => status == 200);
        Assert.Contains(409, result);
        Assert.Equal(1, await ActiveAdmins(seed.Org.Id));
    }

    [Fact]
    public async Task StaleUpdatesAndNonAdminWritesAreRejected()
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        await Rejected(403, () => Act(s => s.AddMemberAsync(seed.Platform, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin))));
        var updated = await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Active, null)));
        await Rejected(409, () => Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Viewer, MembershipStatus.Active, null))), "stale_membership");
        await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Viewer, MembershipStatus.Active, updated.UpdatedAt)));
    }

    [Fact]
    public async Task LockTimeoutReturnsConflictWithoutPartialProvisioning()
    {
        var connectionString = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1869636978, 26)", connection, tx);
        await gate.ExecuteNonQueryAsync();
        await Rejected(409, () => Act(s => s.BootstrapAsync(Account("blocked"))), "concurrent_change_retry");
        await tx.RollbackAsync();
        Assert.Equal(0, await InScope((_, db) => db.Users.CountAsync()));
    }

    [Fact]
    public async Task ExistingFirstAdminAccountRollsBackOrganizationWithoutPromotion()
    {
        var seed = await Seed();
        await Rejected(409, () => Act(s => s.CreateOrganizationAsync(seed.Platform, new("Other", "other", "CL", Account("alpha")))), "account_already_exists");
        Assert.Equal(1, await InScope((_, db) => db.Organizations.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.OrganizationMemberships.CountAsync()));
    }

    [Fact]
    public async Task RevokedPlatformGrantIsCheckedAtWriteTime()
    {
        var seed = await Seed();
        await InScope(async (_, db) => { (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return true; });
        await Rejected(403, () => Act(s => s.CreateOrganizationAsync(seed.Platform, new("Denied", "denied", "CL", Account("denied")))));
    }

    [Fact]
    public async Task SuspendedOrganizationAndMembershipCannotAuthorize()
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.OrgAdmin, MembershipStatus.Suspended, null)));
        await Rejected(403, () => Act(s => s.ListMembersAsync(seed.Platform, seed.Org.Id)));
        await InScope(async (_, db) => { (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended; await db.SaveChangesAsync(); return true; });
        await Rejected(403, () => Act(s => s.ListMembersAsync(seed.Admin, seed.Org.Id)));
    }
    private HttpClient Client() => app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Csrf(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }
    private static async Task Login(HttpClient client, string name)
    {
        await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = Account(name).Email, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Csrf(client); // CSRF token is bound to the authenticated identity.
    }

    [Fact]
    public async Task HttpRequiresAuthenticationCsrfAndTenantPermissions()
    {
        var seed = await Seed();
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/login", new { Email = Account("alpha").Email, Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/auth/register")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/auth/bootstrap")).StatusCode);
        using var platform = Client();
        await Login(platform, "platform");
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/organizations/{seed.Org.Id}/memberships")).StatusCode);
        using var admin = Client();
        await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/organizations/{seed.Org.Id}/memberships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganization("Denied", "denied", "CL", Account("denied")))).StatusCode);
        var last = await admin.PatchAsJsonAsync($"/api/organizations/{seed.Org.Id}/memberships/{seed.Membership}", new ChangeMember(OrganizationRole.Viewer, MembershipStatus.Active, null));
        Assert.Equal(HttpStatusCode.Conflict, last.StatusCode);
    }

    [Fact]
    public async Task HttpCreatesOrganizationAndRejectsRevokedSession()
    {
        var seed = await Seed();
        using var platform = Client();
        await Login(platform, "platform");
        var response = await platform.PostAsJsonAsync("/api/organizations", new CreateOrganization("Beta", "beta", "CL", Account("beta")));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var beta = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId != seed.Org.Id));
        using var betaClient = Client();
        await Login(betaClient, "beta");
        Assert.Equal(HttpStatusCode.Forbidden, (await betaClient.GetAsync($"/api/organizations/{seed.Org.Id}/memberships")).StatusCode);
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(beta.UserId, OrganizationRole.OrgAdmin)));
        using var alphaClient = Client();
        await Login(alphaClient, "alpha");
        await Act(async s => { await s.SetUserActiveAsync(seed.Platform, seed.Admin, false); return true; });
        Assert.Equal(HttpStatusCode.Unauthorized, (await alphaClient.GetAsync("/api/organizations")).StatusCode);
    }

    [Fact]
    public async Task InvalidLoginLocksAccountAndDoesNotAuthenticate()
    {
        await Seed();
        using var client = Client();
        await Csrf(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { Email = Account("alpha").Email, Password = "WrongPassword!2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { Email = Account("alpha").Email, Password })).StatusCode);
    }
}
