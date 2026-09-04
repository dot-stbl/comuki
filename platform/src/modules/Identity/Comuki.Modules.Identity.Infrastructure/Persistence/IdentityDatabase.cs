namespace Comuki.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Physical identity database — the Postgres schema name plus every table
/// that belongs to it. Single source every <c>IEntityTypeConfiguration</c>
/// reads; no magic strings in <c>builder.ToTable(...)</c>. The migration
/// history table lives at <c>identity.__ef_migrations_history</c> (per the
/// EF Core Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="IdentityDbContext.ApplyOptions"/>.
/// </summary>
public static class IdentityDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "identity";

    /// <summary>User accounts.</summary>
    public const string Users = "users";

    /// <summary>API keys (prefix + HMAC, never the secret).</summary>
    public const string ApiKeys = "api_keys";

    /// <summary>Role assignments (subject × role × scope).</summary>
    public const string RoleAssignments = "role_assignments";

    /// <summary>OIDC identity links.</summary>
    public const string OidcLinks = "oidc_links";

    /// <summary>In-flight OIDC authorization states (PKCE + returnTo binding).</summary>
    public const string OidcStates = "oidc_states";
}
