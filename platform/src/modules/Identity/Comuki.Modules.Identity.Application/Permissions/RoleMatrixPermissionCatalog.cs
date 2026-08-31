using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Roles;

namespace Comuki.Modules.Identity.Application.Permissions;

/// <summary>
/// Catalog over the compiled <see cref="RoleMatrix"/> — the whole
/// vocabulary, materialized once. Singleton: immutable data, no I/O.
/// </summary>
public sealed class RoleMatrixPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlySet<PermissionKey> keys = RoleMatrix.AllPermissionKeys;

    /// <inheritdoc />
    public IReadOnlySet<PermissionKey> AllKeys => keys;

    /// <inheritdoc />
    public bool Contains(PermissionKey key)
    {
        return keys.Contains(key);
    }
}
