namespace Comuki.Modules.Identity.Domain.Roles;

/// <summary>
/// The fixed role set — roles live in code only (scope-draft §10: no
/// custom roles, no DB role rows). The wire/stored form is the kebab-case
/// key from <see cref="RoleKeys"/>; assignments reference the key.
/// </summary>
public enum Role
{
    /// <summary>Break-glass platform administrator — every permission.</summary>
    PlatformAdmin = 0,

    /// <summary>Platform operator — runs the fleet, no identity management.</summary>
    Operator = 1,

    /// <summary>Administrator of one project.</summary>
    ProjectAdmin = 2,

    /// <summary>Approves plans inside a project.</summary>
    Approver = 3,

    /// <summary>Works inside a project.</summary>
    Member = 4,

    /// <summary>Read-only access inside a project.</summary>
    Viewer = 5,
}
