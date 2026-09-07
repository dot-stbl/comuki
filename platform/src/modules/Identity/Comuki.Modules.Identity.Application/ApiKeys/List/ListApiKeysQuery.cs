using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys.List;

/// <summary>
/// Read-side query for the identity-admin api-key list
/// (<c>GET /api/v1/keys</c>, issue #45 / F13). The host owns pagination
/// bounds and parses the optional <c>userId</c> query parameter.
/// </summary>
/// <param name="UserId">Optional owner user id filter; <c>null</c> for any.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size (1..100).</param>
public sealed record ListApiKeysQuery(
    UserId? UserId,
    int Page,
    int PageSize);
