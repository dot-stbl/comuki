using Comuki.Modules.Identity.Domain.Permissions;

namespace Comuki.Modules.Identity.Application.Permissions;

/// <summary>
/// The permission vocabulary as the enforcement side sees it: one place
/// that answers whether a demanded key is declared. Built from the code
/// <see cref="Domain.Roles.RoleMatrix"/> at startup — no registration
/// ceremony, no plugin surface (the vocabulary is the platform's own).
/// </summary>
public interface IPermissionCatalog
{
    /// <summary>Every declared key.</summary>
    public IReadOnlySet<PermissionKey> AllKeys { get; }

    /// <summary>Whether the catalog declares the key.</summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool Contains(PermissionKey key);
}
