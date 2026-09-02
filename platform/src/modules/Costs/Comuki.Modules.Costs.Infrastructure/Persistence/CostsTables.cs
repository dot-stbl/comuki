namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>
/// Physical Costs table names — the single source every EF configuration
/// reads. The module is the 7th context migrating the shared database
/// (after orchestration, identity, projects, memory, chat, intake).
/// </summary>
public static class CostsTables
{
    /// <summary>Metered usage events (tokens + cost).</summary>
    public const string UsageEvents = "usage_events";

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_costs";
}
