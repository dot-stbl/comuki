namespace Comuki.Shared.Migrations;

/// <summary>
/// Outcome of one <see cref="ComukiDatabaseMigrator.EnsureAllAsync"/> pass:
/// which schemas had pending migrations applied. An up-to-date database
/// yields an empty <see cref="AppliedSchemas"/> list.
/// </summary>
public sealed record DatabaseMigrationSummary
{
    /// <summary>One entry per schema that had at least one migration applied, in execution order.</summary>
    public required IReadOnlyList<SchemaMigrationsApplied> AppliedSchemas { get; init; }

    /// <summary>Total number of migrations applied across every schema.</summary>
    public int TotalApplied => AppliedSchemas.Sum(static schema => schema.Migrations.Count);

    /// <summary>One schema's applied migrations.</summary>
    /// <param name="Schema">The schema label (e.g. <c>orchestration</c>).</param>
    /// <param name="Migrations">The migration names applied by this pass.</param>
    public sealed record SchemaMigrationsApplied(string Schema, IReadOnlyList<string> Migrations);
}
