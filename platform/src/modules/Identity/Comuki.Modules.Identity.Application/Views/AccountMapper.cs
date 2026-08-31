using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Views;

/// <summary>
/// Entity → view mapping, hand-written and pure (no DI, no source
/// generator — the module has no Mapperly toolchain). One home for every
/// projection so handlers stay one-liners.
/// </summary>
public static class AccountMapper
{
    /// <summary>Maps a user entity to its read model.</summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public static UserAccountView ToView(User user)
    {
        return new UserAccountView(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Disabled,
            user.TokensVersion,
            user.CreatedAt);
    }

    /// <summary>Maps an assignment entity to its read model.</summary>
    /// <param name="assignment"></param>
    /// <returns></returns>
    public static RoleAssignmentView ToView(RoleAssignment assignment)
    {
        return new RoleAssignmentView(
            assignment.Id,
            RoleKeys.Key(assignment.Role),
            ScopeLevelKeys.Key(assignment.ScopeLevel),
            assignment.ScopeProjectId?.ToString(),
            SubjectTypeKeys.Key(assignment.SubjectType),
            assignment.SubjectId.ToString(),
            assignment.GrantedBy?.ToString(),
            assignment.CreatedAt,
            assignment.RevokedAt,
            assignment.IsActive);
    }
}
