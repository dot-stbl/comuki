namespace Comuki.Modules.Identity.Application.Permissions;

/// <summary>
/// Marks an endpoint as demanding one permission key — the action axis of
/// the authorization model. The object axis (out-of-scope rows) is not
/// this attribute's business: those surface as 404, never as a deny.
/// Validated against <see cref="IPermissionCatalog"/> at startup: a key
/// nobody declares fails the boot, not the first request.
/// </summary>
/// <param name="permissionKey">A key the catalog declares, e.g. <c>run:stop</c>.</param>
/// <remarks>
/// Single key, on purpose: "A and B" is a third key the platform declares;
/// "A or B" has no honest reader in a role editor. Endpoints that need no
/// permission (health, login) carry no attribute at all. The attribute
/// enforces nothing by itself — the global resource filter installed by
/// the Identity infrastructure reads it off the endpoint metadata, so
/// controllers depend on the Application layer only.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresPermissionAttribute(string permissionKey) : Attribute
{
    /// <summary>The permission key the endpoint demands.</summary>
    public string PermissionKey { get; } = permissionKey;
}
