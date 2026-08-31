namespace Comuki.Modules.Identity.Domain.Roles;

/// <summary>
/// The stable kebab-case key of each <see cref="Role"/> — the value stored
/// in <c>role_assignments.role</c> and shown in the UI. Mapping between the
/// enum and its key lives here and nowhere else.
/// </summary>
public static class RoleKeys
{
    /// <summary>Key of <see cref="Role.PlatformAdmin"/>.</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Key of <see cref="Role.Operator"/>.</summary>
    public const string Operator = "operator";

    /// <summary>Key of <see cref="Role.ProjectAdmin"/>.</summary>
    public const string ProjectAdmin = "project-admin";

    /// <summary>Key of <see cref="Role.Approver"/>.</summary>
    public const string Approver = "approver";

    /// <summary>Key of <see cref="Role.Member"/>.</summary>
    public const string Member = "member";

    /// <summary>Key of <see cref="Role.Viewer"/>.</summary>
    public const string Viewer = "viewer";

    /// <summary>Returns the key of a role; total over the enum.</summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public static string Key(Role role)
    {
        return role switch
        {
            Role.PlatformAdmin => PlatformAdmin,
            Role.Operator => Operator,
            Role.ProjectAdmin => ProjectAdmin,
            Role.Approver => Approver,
            Role.Member => Member,
            Role.Viewer => Viewer,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    /// <summary>Parses a stored key back into a role; null when unknown.</summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static Role? Parse(string key)
    {
        return key switch
        {
            PlatformAdmin => Role.PlatformAdmin,
            Operator => Role.Operator,
            ProjectAdmin => Role.ProjectAdmin,
            Approver => Role.Approver,
            Member => Role.Member,
            Viewer => Role.Viewer,
            _ => null,
        };
    }
}
