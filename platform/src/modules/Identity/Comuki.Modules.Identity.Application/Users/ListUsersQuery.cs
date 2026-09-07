namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Read-side query for the identity-admin users list
/// (<c>GET /api/v1/users</c>, issue #45 / F13). The host owns pagination
/// bounds — handlers receive already-validated <c>page</c> and
/// <c>pageSize</c>. Pagination is 1-based.
/// </summary>
/// <param name="EmailContains">Optional substring filter on email (case-insensitive).</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size (1..100).</param>
public sealed record ListUsersQuery(
    string? EmailContains,
    int Page,
    int PageSize);
