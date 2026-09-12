namespace Comuki.Migrator.Status;

/// <summary>
/// Dry-run outcome of one schema for <c>comuki-migrator status</c>
/// (issue #56 §3): the schema label and its pending migrations.
/// </summary>
/// <param name="Label">Schema label (orchestration, identity, …).</param>
/// <param name="PendingMigrations">Migration ids not yet applied, in apply order.</param>
internal sealed record MigratorSchemaStatus(string Label, IReadOnlyList<string> PendingMigrations);

/// <summary>
/// Rendering and exit-code contract of <c>comuki-migrator status</c>
/// (issue #56 §3): 0 = every schema up to date, 1 = at least one pending
/// migration, 2 = error (the Program wrapper maps caught exceptions).
/// Output mirrors the <c>applied (label): …</c> shape of the apply path:
/// <c>pending (label): …</c> lines, <c>label schema is up to date</c>
/// otherwise.
/// </summary>
internal static class MigratorStatusReport
{
    /// <summary>Every schema applied — success for CI gates.</summary>
    public const int UpToDateExitCode = 0;

    /// <summary>At least one pending migration.</summary>
    public const int PendingExitCode = 1;

    /// <summary>A schema probe failed (unreachable database, and so on).</summary>
    public const int ErrorExitCode = 2;

    /// <summary>Exit code for a completed (non-errored) run over every schema.</summary>
    /// <param name="totalPending">Sum of pending migrations across schemas.</param>
    public static int ExitCode(int totalPending)
    {
        return totalPending > 0 ? PendingExitCode : UpToDateExitCode;
    }

    /// <summary>Lines of one schema's dry-run: pending entries, or the up-to-date line.</summary>
    /// <param name="schema">The schema outcome to render.</param>
    public static IEnumerable<string> RenderLines(MigratorSchemaStatus schema)
    {
        foreach (var migration in schema.PendingMigrations)
        {
            yield return $"pending ({schema.Label}): {migration}";
        }

        if (schema.PendingMigrations.Count == 0)
        {
            yield return $"{schema.Label} schema is up to date";
        }
    }
}
