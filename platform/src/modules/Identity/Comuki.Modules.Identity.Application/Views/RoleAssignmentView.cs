using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.Views;

/// <summary>
/// Read model of a role assignment. Wire-friendly strings (role key,
/// scope key) — the API never leaks enum names.
/// </summary>
/// <param name="Id"></param>
/// <param name="Role"></param>
/// <param name="ScopeLevel"></param>
/// <param name="ScopeProjectId"></param>
/// <param name="SubjectType"></param>
/// <param name="SubjectId"></param>
/// <param name="GrantedBy"></param>
/// <param name="CreatedAt"></param>
/// <param name="RevokedAt"></param>
/// <param name="IsActive"></param>
public sealed record RoleAssignmentView(
    RoleAssignmentId Id,
    string Role,
    string ScopeLevel,
    string? ScopeProjectId,
    string SubjectType,
    string SubjectId,
    string? GrantedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive);
