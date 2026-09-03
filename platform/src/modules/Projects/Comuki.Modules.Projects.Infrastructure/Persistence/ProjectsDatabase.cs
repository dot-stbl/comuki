namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// Physical Projects database — the Postgres schema name plus every table
/// that belongs to it. Single source every <c>IEntityTypeConfiguration</c>
/// reads; no magic strings in <c>builder.ToTable(...)</c>. The migration
/// history table lives at <c>projects.__ef_migrations_history</c> (per the
/// EF Core Postgres convention) and is configured via
/// <c>npgsql.MigrationsHistoryTable(name, schema)</c> in
/// <see cref="ProjectsDbContext.ApplyOptions"/>.
/// </summary>
public static class ProjectsDatabase
{
    /// <summary>Postgres schema name. the namespace.</summary>
    public const string Schema = "projects";

    /// <summary>Projects.</summary>
    public const string Projects = "projects";

    /// <summary>Per-project settings.</summary>
    public const string ProjectSettings = "project_settings";
}
