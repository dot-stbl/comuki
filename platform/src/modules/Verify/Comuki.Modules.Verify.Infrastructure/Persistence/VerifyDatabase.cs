namespace Comuki.Modules.Verify.Infrastructure.Persistence;

/// <summary>
/// Physical Verify database — the Postgres schema name plus the single
/// table the module owns. Single source every
/// <c>IEntityTypeConfiguration</c> reads; no magic strings in
/// <c>builder.ToTable(...)</c>. The migration history table lives at
/// <c>verify.__ef_migrations_history</c> (per the EF Core Postgres
/// convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="VerifyDbContext.ApplyOptions"/>.
/// </summary>
public static class VerifyDatabase
{
    /// <summary>Postgres schema name.</summary>
    public const string Schema = "verify";

    /// <summary>Generic-command verification runs — the only table this module owns.</summary>
    public const string GenericCommandRuns = "generic_command_runs";
}
