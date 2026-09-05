using System.Collections.Frozen;
using Comuki.Modules.Identity.Domain.Permissions;

using static Comuki.Modules.Identity.Domain.Permissions.Permissions;

namespace Comuki.Modules.Identity.Domain.Roles;

/// <summary>
/// The role→permissions matrix and the seniority ladder — compiled, not
/// data (scope-draft §10: roles are fixed in code; the database holds
/// assignments only). Changing what a role can do is a commit, a build
/// and a release; that is the deliberate trade against custom roles.
/// </summary>
public static class RoleMatrix
{
    private static readonly FrozenDictionary<Role, FrozenSet<PermissionKey>> permissionsByRole =
        new Dictionary<Role, PermissionKey[]>
        {
            [Role.PlatformAdmin] =
            [
                RunRead, RunCreate, RunStop, RunInject,
                PlanRead, PlanApprove,
                QueueRead,
                IntakeRead, IntakeClaim,
                SourceRead, SourceWrite,
                ChatUse,
                SettingsRead, SettingsWrite,
                KnowledgeRead, KnowledgeWrite, KnowledgeAdmin,
                VerifyRead,
                CostRead,
                ProjectRead, ProjectAdmin,
                IdentityRead, IdentityWrite,
                PlatformAdmin,
            ],
            [Role.Operator] =
            [
                RunRead, RunCreate, RunStop, RunInject,
                PlanRead,
                QueueRead,
                IntakeRead, IntakeClaim,
                SourceRead, SourceWrite,
                ChatUse,
                SettingsRead, SettingsWrite,
                KnowledgeRead, KnowledgeWrite,
                VerifyRead,
                CostRead,
                ProjectRead,
            ],
            [Role.ProjectAdmin] =
            [
                RunRead, RunCreate, RunStop, RunInject,
                PlanRead,
                QueueRead,
                IntakeRead,
                SourceRead, SourceWrite,
                ChatUse,
                SettingsRead, SettingsWrite,
                KnowledgeRead, KnowledgeWrite,
                VerifyRead,
                CostRead,
                ProjectRead,
                IdentityRead,
            ],
            [Role.Approver] =
            [
                RunRead,
                PlanRead, PlanApprove,
                QueueRead,
                VerifyRead,
                CostRead,
                ProjectRead,
            ],
            [Role.Member] =
            [
                RunRead, RunCreate,
                PlanRead,
                QueueRead,
                IntakeRead,
                SourceRead, SourceWrite,
                ChatUse,
                SettingsRead,
                KnowledgeRead,
                VerifyRead,
                ProjectRead,
            ],
            [Role.Viewer] =
            [
                RunRead,
                PlanRead,
                QueueRead,
                IntakeRead,
                SourceRead,
                SettingsRead,
                KnowledgeRead,
                VerifyRead,
                CostRead,
                ProjectRead,
            ],
        }
        .ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value.ToFrozenSet());

    private static readonly FrozenDictionary<Role, int> seniorityByRole = new Dictionary<Role, int>
    {
        // Ladder with gaps — a new role slots in without renumbering.
        [Role.PlatformAdmin] = 100,
        [Role.Operator] = 70,
        [Role.ProjectAdmin] = 60,
        [Role.Approver] = 40,
        [Role.Member] = 30,
        [Role.Viewer] = 20,
    }
    .ToFrozenDictionary();

    private static readonly FrozenSet<PermissionKey> allKeys =
        permissionsByRole.Values.SelectMany(static set => set).ToFrozenSet();

    /// <summary>Every key the vocabulary declares (union of all roles).</summary>
    public static IReadOnlySet<PermissionKey> AllPermissionKeys => allKeys;

    /// <summary>The permission set a role carries — read-only, code-declared.</summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public static IReadOnlySet<PermissionKey> PermissionsOf(Role role)
    {
        return permissionsByRole.TryGetValue(role, out var permissions) ? permissions : [];
    }

    /// <summary>The role's seniority (higher = more senior). Total over the enum.</summary>
    /// <param name="role"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">Unknown role.</exception>
    public static int SeniorityOf(Role role)
    {
        return seniorityByRole.TryGetValue(role, out var seniority)
            ? seniority
            : throw new ArgumentOutOfRangeException(nameof(role), role, null);
    }
}
