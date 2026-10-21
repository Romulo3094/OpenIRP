using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence;

/// <summary>
/// First implementation slice: Identity and tenant membership model.
/// No schema changes occur until a reviewed EF Core migration is applied.
/// </summary>
public sealed class OpenIrpDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public OpenIrpDbContext(DbContextOptions<OpenIrpDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<PlatformAdministrator> PlatformAdministrators => Set<PlatformAdministrator>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentity(builder);
        ConfigureOrganization(builder);
        ConfigureMembership(builder);
        ConfigurePlatformAdministrator(builder);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("asp_net_users", "auth");
            b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at").HasColumnType("timestamp with time zone");
            // Identity's conventional normalized-email index is not unique.
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("asp_net_roles", "auth");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles", "auth");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims", "auth");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins", "auth");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims", "auth");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens", "auth");
    }

    private static void ConfigureOrganization(ModelBuilder builder)
    {
        builder.Entity<Organization>(b =>
        {
            b.ToTable("organizations", "app", t =>
            {
                t.HasCheckConstraint("ck_organizations_status", "status IN ('ACTIVE','SUSPENDED','INACTIVE')");
                t.HasCheckConstraint("ck_organizations_country", "country_code ~ '^[A-Z]{2}$'");
                t.HasCheckConstraint("ck_organizations_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
            b.Property(x => x.Sector).HasColumnName("sector").HasMaxLength(100);
            b.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
            b.Property(x => x.SizeBand).HasColumnName("size_band").HasMaxLength(30);
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
                .HasConversion(v => v.ToString().ToUpperInvariant(),
                    v => Enum.Parse<OrganizationStatus>(v, true))
                .HasDefaultValue(OrganizationStatus.Active);
            b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_organizations_slug");
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureMembership(ModelBuilder builder)
    {
        builder.Entity<OrganizationMembership>(b =>
        {
            b.ToTable("organization_memberships", "app", t =>
            {
                t.HasCheckConstraint("ck_memberships_role", "role IN ('ORG_ADMIN','ANALYST','VIEWER')");
                t.HasCheckConstraint("ck_memberships_status", "status IN ('ACTIVE','SUSPENDED','REVOKED')");
                t.HasCheckConstraint("ck_memberships_revocation", "status <> 'REVOKED' OR revoked_at IS NOT NULL");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.OrganizationId).HasColumnName("organization_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.Role).HasColumnName("role").HasMaxLength(20)
                .HasConversion(v => v switch
                    {
                        OrganizationRole.OrgAdmin => "ORG_ADMIN",
                        OrganizationRole.Analyst => "ANALYST",
                        OrganizationRole.Viewer => "VIEWER",
                        _ => throw new ArgumentOutOfRangeException(nameof(v))
                    },
                    v => v switch
                    {
                        "ORG_ADMIN" => OrganizationRole.OrgAdmin,
                        "ANALYST" => OrganizationRole.Analyst,
                        "VIEWER" => OrganizationRole.Viewer,
                        _ => throw new ArgumentOutOfRangeException(nameof(v))
                    });
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
                .HasConversion(v => v.ToString().ToUpperInvariant(),
                    v => Enum.Parse<MembershipStatus>(v, true))
                .HasDefaultValue(MembershipStatus.Active);
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            b.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique()
                .HasDatabaseName("ux_memberships_org_user");
            b.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ux_memberships_org_id");
            b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePlatformAdministrator(ModelBuilder builder)
    {
        builder.Entity<PlatformAdministrator>(b =>
        {
            b.ToTable("platform_administrators", "app");
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.GrantedAt).HasColumnName("granted_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
            b.HasOne<ApplicationUser>().WithOne().HasForeignKey<PlatformAdministrator>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.GrantedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RevokedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
