using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Oidc;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping Identity id value objects (and the Kernel
/// <see cref="ProjectId"/>) to <c>uuid</c> columns, plus the enum-as-key
/// converters for role/scope/subject columns.
/// </summary>
public static class IdentityIdConverters
{
    /// <summary><see cref="UserId"/> uuid converter.</summary>
    public static readonly ValueConverter<UserId, Guid> UserIdToUuid = new(
        static id => id.Value,
        static value => new UserId(value));

    /// <summary><see cref="ApiKeyId"/> uuid converter.</summary>
    public static readonly ValueConverter<ApiKeyId, Guid> ApiKeyIdToUuid = new(
        static id => id.Value,
        static value => new ApiKeyId(value));

    /// <summary><see cref="RoleAssignmentId"/> uuid converter.</summary>
    public static readonly ValueConverter<RoleAssignmentId, Guid> RoleAssignmentIdToUuid = new(
        static id => id.Value,
        static value => new RoleAssignmentId(value));

    /// <summary><see cref="OidcLinkId"/> uuid converter.</summary>
    public static readonly ValueConverter<OidcLinkId, Guid> OidcLinkIdToUuid = new(
        static id => id.Value,
        static value => new OidcLinkId(value));

    /// <summary><see cref="OidcStateId"/> uuid converter.</summary>
    public static readonly ValueConverter<OidcStateId, Guid> OidcStateIdToUuid = new(
        static id => id.Value,
        static value => new OidcStateId(value));

    /// <summary>Kernel <see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));
}
