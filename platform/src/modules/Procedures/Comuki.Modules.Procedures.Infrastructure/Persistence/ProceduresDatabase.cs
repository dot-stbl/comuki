namespace Comuki.Modules.Procedures.Infrastructure.Persistence;

/// <summary>
/// Physical Procedures database — the Postgres schema name plus the
/// single source every <c>IEntityTypeConfiguration</c> reads. The schema
/// holds the procedure definitions, the layered project policy, the
/// versioned compiled plans, and the resolution snapshot. No magic
/// strings in <c>builder.ToTable(...)</c> — every table name resolves
/// to a constant here. The migration history table lives at
/// <c>procedures.__comuki_procedures</c>, deviating from the older
/// <c>__ef_migrations_history</c> naming used by the very first
/// modules (per Repositories: each module keeps its own per-schema
/// history so all module contexts migrate one database without
/// colliding).
/// </summary>
public static class ProceduresDatabase
{
    /// <summary>Postgres schema name.</summary>
    public const string Schema = "procedures";

    /// <summary>The migrations history table inside the <see cref="Schema"/> schema.</summary>
    public const string MigrationsHistoryTable = "__comuki_procedures";

    /// <summary>Table names — every IEntityTypeConfiguration reads from here.</summary>
    public static class Tables
    {
        /// <summary>Immutable compiled procedure versions (content-addressed PK).</summary>
        public const string CompiledProcedureVersions = "compiled_procedure_versions";
    }
}
