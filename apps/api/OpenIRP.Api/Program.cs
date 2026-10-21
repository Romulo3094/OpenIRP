using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Infrastructure.Identity;
using OpenIRP.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("OpenIRP")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Configure ConnectionStrings:OpenIRP using .NET user-secrets or an environment variable.");

builder.Services.AddDbContext<OpenIrpDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<OpenIrpDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Authentication routes and initial PLATFORM_ADMIN provisioning will be implemented
// in the next increment. Do not expose self-registration or auto-seed privileged users.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
