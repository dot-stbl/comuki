namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>
/// Physical Costs database — the Postgres schema name plus every table that
/// belongs to it. Single source every <c>IEntityTypeConfiguration</c> reads;
/// no magic strings in <c>builder.ToTable(...)</c>. The migration history
/// table lives at <c>costs.__ef_migrations_history</c> (per the EF Core
/// Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="CostsDbContext.ApplyOptions"/>.
/// </summary>
public static class CostsDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "costs";

    /// <summary>Metered usage events (tokens + cost).</summary>
    public const string UsageEvents = "usage_events";
}
