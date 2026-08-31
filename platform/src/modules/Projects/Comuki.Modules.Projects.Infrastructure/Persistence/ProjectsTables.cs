namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// Physical Projects table names — the single source every EF
/// configuration reads; no magic strings in <c>IEntityTypeConfiguration</c>.
/// Includes the migrations history table: the module keeps its own
/// history (separate from the orchestration and identity contexts) so the
/// contexts can migrate one database without colliding.
/// </summary>
public static class ProjectsTables
{
    /// <summary>Projects.</summary>
    public const string Projects = "projects";

    /// <summary>Per-project settings.</summary>
    public const string ProjectSettings = "project_settings";

    /// <summary>Module-private EF migrations history table.</summary>
    public const string MigrationsHistory = "__comuki_projects";
}
