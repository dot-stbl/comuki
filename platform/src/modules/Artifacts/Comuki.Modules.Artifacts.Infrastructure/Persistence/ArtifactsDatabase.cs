namespace Comuki.Modules.Artifacts.Infrastructure.Persistence;

/// <summary>
/// Physical artifacts database — the Postgres schema name plus every
/// table that belongs to it. Single source every
/// <c>IEntityTypeConfiguration</c> reads; no magic strings in
/// <c>builder.ToTable(...)</c>. The migration history table lives at
/// <c>artifacts.__ef_migrations_history</c> per the EF Core Postgres
/// convention configured via
/// <see cref="ArtifactsDbContext.ApplyOptions"/>.
/// </summary>
public static class ArtifactsDatabase
{
    /// <summary>Postgres schema name — the namespace.</summary>
    public const string Schema = "artifacts";

    /// <summary>One row per packaged run — the packager's bookkeeping.</summary>
    public const string RunBundles = "run_bundles";
}
