namespace Comuki.Engine.Orchestration.Infrastructure.Persistence;

/// <summary>
/// Physical orchestration table names — the single source every EF
/// configuration reads; no magic strings in <c>IEntityTypeConfiguration</c>.
/// </summary>
public static class OrchestrationTables
{
    /// <summary>Run aggregate root.</summary>
    public const string Runs = "runs";

    /// <summary>Work item queue rows.</summary>
    public const string WorkItems = "work_items";

    /// <summary>Plan DAG edges between work items.</summary>
    public const string WorkItemDependencies = "work_item_dependencies";

    /// <summary>Append-only run journal.</summary>
    public const string RunEvents = "run_events";
}
