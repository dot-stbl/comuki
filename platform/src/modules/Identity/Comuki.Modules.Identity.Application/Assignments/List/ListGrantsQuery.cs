using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Modules.Identity.Application.Assignments.List;

/// <summary>
/// Read-side query for the identity-admin role-assignment list
/// (<c>GET /api/v1/grants</c>, issue #45 / F13). The host owns pagination
/// bounds and turns the optional <c>userId</c> query parameter into a
/// filter that constrains <see cref="SubjectKind"/> to <c>user</c>.
/// </summary>
/// <param name="SubjectKind">Optional subject kind filter; <c>null</c> for any.</param>
/// <param name="SubjectId">Optional subject id filter; <c>null</c> for any.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size (1..100).</param>
public sealed record ListGrantsQuery(
    SubjectType? SubjectKind,
    Guid? SubjectId,
    int Page,
    int PageSize);
