namespace Comuki.Modules.Repositories.Infrastructure.Persistence;

/// <summary>
/// Physical Repositories database — the Postgres schema name plus every
/// table the module owns. Single source every <c>IEntityTypeConfiguration</c>
/// reads; no magic strings in <c>builder.ToTable(...)</c>. The migration
/// history table lives at <c>repositories.__comuki_repositories</c>; this
/// module intentionally deviates from the older <c>__ef_migrations_history</c>
/// naming used by sibling modules so all module contexts migrating one
/// database can declare a stable, per-module history without colliding.
/// The history table is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="RepositoriesDbContext.ApplyOptions"/>.
/// </summary>
public static class RepositoriesDatabase
{
    /// <summary>Postgres schema name.</summary>
    public const string Schema = "repositories";

    /// <summary>Registered repositories (one row per unique (host, url) pair).</summary>
    public const string Repositories = "repositories";

    /// <summary>Per-Repository rule snapshot (protected branches, required checks, approvers).</summary>
    public const string RepositoryPolicies = "repository_policies";

    /// <summary>Per-Repository credential reference and its default access level.</summary>
    public const string RepositoryCredentialRefs = "repository_credential_refs";
}
