using Comuki.Modules.Identity.Application.Views;

namespace Comuki.Host.Auth.Models;

/// <summary>
/// Paged envelope shared by the identity-admin list endpoints
/// (issue #45 / F13 — <c>GET /api/v1/users</c>, <c>/api/v1/grants</c>,
/// <c>/api/v1/keys</c>). Wire shape <c>{ items, total }</c>: <c>items</c> is
/// the already-projected view list for the requested page; <c>total</c> is
/// the count across every page, not the page size.
/// </summary>
/// <param name="Items">Page contents.</param>
/// <param name="Total">Total rows across all pages.</param>
public sealed record IdentityAdminPage(
    IReadOnlyList<UserAccountView> Items,
    int Total);

/// <summary>Paged envelope for role assignments.</summary>
/// <param name="Items">Page contents.</param>
/// <param name="Total">Total rows across all pages.</param>
public sealed record IdentityAdminGrantsPage(
    IReadOnlyList<RoleAssignmentView> Items,
    int Total);

/// <summary>Paged envelope for API keys. The plaintext is never carried —
/// only the public-facing view record (prefix, name, status…).</summary>
/// <param name="Items">Page contents.</param>
/// <param name="Total">Total rows across all pages.</param>
public sealed record IdentityAdminKeysPage(
    IReadOnlyList<Modules.Identity.Application.ApiKeys.ApiKeyView> Items,
    int Total);
