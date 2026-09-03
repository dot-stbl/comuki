namespace Comuki.Engine.Orchestration.Infrastructure.Persistence;

/// <summary>
/// Physical orchestration database — the Postgres schema name plus every
/// table that belongs to it. Single source every <c>IEntityTypeConfiguration</c>
/// reads; no magic strings in <c>builder.ToTable(...)</c>. The migration
/// history table lives at <c>orchestration.__ef_migrations_history</c> (per
/// the EF Core Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="OrchestrationDbContext.ApplyOptions"/>.
/// </summary>
public static class OrchestrationDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "orchestration";

    /// <summary>Run aggregate root.</summary>
    public const string Runs = "runs";

    /// <summary>Work item queue rows.</summary>
    public const string WorkItems = "work_items";

    /// <summary>Plan DAG edges between work items.</summary>
    public const string WorkItemDependencies = "work_item_dependencies";

    /// <summary>Append-only run journal.</summary>
    public const string RunEvents = "run_events";
}
