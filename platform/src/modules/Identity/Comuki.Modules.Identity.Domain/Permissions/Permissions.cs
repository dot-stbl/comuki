namespace Comuki.Modules.Identity.Domain.Permissions;

/// <summary>
/// The baseline permission vocabulary (scope-draft §10): every key the
/// platform demands anywhere, declared in one place. Each key must be
/// assigned to at least one role in <see cref="Roles.RoleMatrix"/> — a
/// unit test holds that invariant. New keys land here first, then in the
/// matrix; the startup validator catches demands that reference neither.
/// </summary>
public static class Permissions
{
    /// <summary>Read runs.</summary>
    public static readonly PermissionKey RunRead = new("run:read");

    /// <summary>Create runs.</summary>
    public static readonly PermissionKey RunCreate = new("run:create");

    /// <summary>Stop runs.</summary>
    public static readonly PermissionKey RunStop = new("run:stop");

    /// <summary>Inject a message into a running run.</summary>
    public static readonly PermissionKey RunInject = new("run:inject");

    /// <summary>Read plans.</summary>
    public static readonly PermissionKey PlanRead = new("plan:read");

    /// <summary>Approve plans.</summary>
    public static readonly PermissionKey PlanApprove = new("plan:approve");

    /// <summary>Read the work-item queue.</summary>
    public static readonly PermissionKey QueueRead = new("queue:read");

    /// <summary>Read intake sources.</summary>
    public static readonly PermissionKey IntakeRead = new("intake:read");

    /// <summary>Claim intake items.</summary>
    public static readonly PermissionKey IntakeClaim = new("intake:claim");

    /// <summary>Read source repositories.</summary>
    public static readonly PermissionKey SourceRead = new("source:read");

    /// <summary>Write to source repositories (branches, PRs).</summary>
    public static readonly PermissionKey SourceWrite = new("source:write");

    /// <summary>Use chat.</summary>
    public static readonly PermissionKey ChatUse = new("chat:use");

    /// <summary>Read settings.</summary>
    public static readonly PermissionKey SettingsRead = new("settings:read");

    /// <summary>Write settings.</summary>
    public static readonly PermissionKey SettingsWrite = new("settings:write");

    /// <summary>Read the knowledge base.</summary>
    public static readonly PermissionKey KnowledgeRead = new("knowledge:read");

    /// <summary>Administer the knowledge base.</summary>
    public static readonly PermissionKey KnowledgeAdmin = new("knowledge:admin");

    /// <summary>Read verification results.</summary>
    public static readonly PermissionKey VerifyRead = new("verify:read");

    /// <summary>Read cost reports.</summary>
    public static readonly PermissionKey CostRead = new("cost:read");

    /// <summary>Read projects.</summary>
    public static readonly PermissionKey ProjectRead = new("project:read");

    /// <summary>Administer projects.</summary>
    public static readonly PermissionKey ProjectAdmin = new("project:admin");

    /// <summary>Read identity (users, assignments, keys).</summary>
    public static readonly PermissionKey IdentityRead = new("identity:read");

    /// <summary>Write identity (grant/revoke, keys, users).</summary>
    public static readonly PermissionKey IdentityWrite = new("identity:write");

    /// <summary>Platform-level administration (everything else is scoped below it).</summary>
    public static readonly PermissionKey PlatformAdmin = new("platform:admin");
}
