using OpenIRP.Application.Incidents;
using OpenIRP.Infrastructure.Incidents;
using OpenIRP.Application.Inventory;
using OpenIRP.Infrastructure.Inventory;
using OpenIRP.Application.Audit;
using OpenIRP.Infrastructure.Audit;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Identity;
using OpenIRP.Infrastructure.Identity;
using OpenIRP.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddDbContext<OpenIrpDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("OpenIRP")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:OpenIRP using user-secrets or an environment variable.");
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
});
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<OpenIrpDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IIdentityAdministration, IdentityAdministration>();
builder.Services.AddScoped<AuditContext>();
builder.Services.AddScoped<AuditRecorder>();
builder.Services.AddScoped<IAuditReader, AuditReader>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IIncidentService, IncidentService>();
var app = builder.Build();

// Explicit offline command: no startup seeding, HTTP listener, implicit migration or public bootstrap route.
if (args.Contains("--bootstrap-platform-admin"))
{
    if (Environment.GetEnvironmentVariable("OPENIRP_BOOTSTRAP_ENABLED") != "true")
    {
        Console.Error.WriteLine("Explicit OPENIRP_BOOTSTRAP_ENABLED=true is required.");
        Environment.ExitCode = 1;
        return;
    }
    var email = Environment.GetEnvironmentVariable("OPENIRP_BOOTSTRAP_EMAIL") ?? "";
    var password = Environment.GetEnvironmentVariable("OPENIRP_BOOTSTRAP_PASSWORD") ?? "";
    Environment.SetEnvironmentVariable("OPENIRP_BOOTSTRAP_PASSWORD", null);
    using var scope = app.Services.CreateScope();
    try
    {
        var id = await scope.ServiceProvider.GetRequiredService<IIdentityAdministration>().BootstrapAsync(new(email, password));
        Console.WriteLine($"Initial PLATFORM_ADMIN provisioned: {id}");
    }
    catch (IdentityOperationException e) { Console.Error.WriteLine(e.Code); Environment.ExitCode = 1; }
    return;
}
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Use(async (context, next) =>
{
    var audit = context.RequestServices.GetRequiredService<AuditContext>();
    context.Response.Headers["X-Correlation-ID"] = audit.CorrelationId.ToString();
    await next();
});
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
// Revalidate account on every request; authorization never trusts cached role claims.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var db = context.RequestServices.GetRequiredService<OpenIrpDbContext>();
        var stamp = context.User.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || !await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp
                && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), context.RequestAborted))
        { context.Response.StatusCode = 401; return; }
    }
    try { await next(); }
    catch (IdentityOperationException e)
    { await Results.Problem(statusCode: e.StatusCode, title: e.Code).ExecuteAsync(context); }
});
app.MapControllers();
app.Run();

public partial class Program { }
