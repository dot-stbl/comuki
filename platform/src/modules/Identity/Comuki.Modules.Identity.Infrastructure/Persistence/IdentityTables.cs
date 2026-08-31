namespace Comuki.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Physical Identity table names — the single source every EF
/// configuration reads; no magic strings in <c>IEntityTypeConfiguration</c>.
/// Includes the migrations history table: the module keeps its own
/// history (separate from the orchestration context's default) so two
/// contexts can migrate one database without colliding.
/// </summary>
public static class IdentityTables
{
    /// <summary>User accounts.</summary>
    public const string Users = "users";

    /// <summary>API keys (prefix + HMAC, never the secret).</summary>
    public const string ApiKeys = "api_keys";

    /// <summary>Role assignments (subject × role × scope).</summary>
    public const string RoleAssignments = "role_assignments";

    /// <summary>OIDC identity links.</summary>
    public const string OidcLinks = "oidc_links";

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_identity";
}
