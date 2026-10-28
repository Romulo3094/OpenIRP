using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenIRP.Infrastructure.Persistence;

/// <summary>
/// EF Core CLI design-time factory. Scaffolding migrations does not open a DB connection.
/// For 'database update', pass ConnectionStrings__OpenIRP through the environment.
/// </summary>
public sealed class OpenIrpDbContextFactory : IDesignTimeDbContextFactory<OpenIrpDbContext>
{
    public OpenIrpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OpenIRP")
            ?? "Host=localhost;Database=openirp_design;Username=design";

        var options = new DbContextOptionsBuilder<OpenIrpDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OpenIrpDbContext(options);
    }
}
