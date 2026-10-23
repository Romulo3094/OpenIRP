using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Identity;

public sealed class IdentityOperationException(int statusCode, string code) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed record NewAccount(string Email, string Password);
public sealed record CreateOrganization(string Name, string Slug, string CountryCode, NewAccount FirstAdministrator);
public sealed record AddMember(Guid UserId, OrganizationRole Role);
public sealed record ChangeMember(OrganizationRole Role, MembershipStatus Status, DateTimeOffset? ExpectedUpdatedAt);
public sealed record OrganizationView(Guid Id, string Name, string Slug, string CountryCode);
public sealed record MembershipView(Guid Id, Guid OrganizationId, Guid UserId, OrganizationRole Role,
    MembershipStatus Status, DateTimeOffset? UpdatedAt);

public interface IIdentityAdministration
{
    Task<Guid> BootstrapAsync(NewAccount account, CancellationToken ct = default);
    Task<OrganizationView> CreateOrganizationAsync(Guid actor, CreateOrganization request, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationView>> ListOrganizationsAsync(Guid actor, CancellationToken ct = default);
    Task<IReadOnlyList<MembershipView>> ListMembersAsync(Guid actor, Guid organization, CancellationToken ct = default);
    Task<MembershipView> AddMemberAsync(Guid actor, Guid organization, AddMember request, CancellationToken ct = default);
    Task<MembershipView> ChangeMemberAsync(Guid actor, Guid organization, Guid membership, ChangeMember request, CancellationToken ct = default);
    Task SetUserActiveAsync(Guid actor, Guid user, bool active, CancellationToken ct = default);
}
