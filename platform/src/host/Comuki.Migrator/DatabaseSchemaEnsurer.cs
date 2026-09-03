using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Npgsql;

namespace Comuki.Migrator;

/// <summary>
/// Idempotent Postgres <c>CREATE SCHEMA IF NOT EXISTS</c>. The Migrator
/// calls <see cref="EnsureAsync"/> before <c>MigrateAsync</c> on each context
/// so the schema is in place when EF Core's per-schema migration history
/// table is queried; <c>CREATE TABLE orchestration.runs (...)</c> that
/// follows succeeds because the schema already exists.
/// </summary>
public static class DatabaseSchemaEnsurer
{
    /// <summary>
    /// Open a fresh connection (so the migrator doesn't have to share its
    /// <c>DbContextOptions</c>), run <c>CREATE SCHEMA IF NOT EXISTS</c> and
    /// close. The schema name is validated against the seven known module
    /// schemas (the same set as the <c>&lt;Module&gt;Database.Schema</c>
    /// consts) and the DDL is dispatched through a switch on the const —
    /// no user input crosses the SQL boundary.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="schema">Schema name. must match a <c>&lt;Module&gt;Database.Schema</c> const.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task EnsureAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        // CA2100 (Review SQL queries for security): the parameter is matched
        // against the seven <Module>Database.Schema consts and the DDL comes
        // out as one of the const DDL consts below. No parameter character
        // ever reaches the SQL string — but the analyzer can't trace that
        // through a switch expression, so the suppression is local and
        // documented.
#pragma warning disable CA2100 // SQL injection: schema is matched against <Module>Database.Schema consts; DDL is a literal const.
        var ddl = schema switch
        {
            OrchestrationDatabase.Schema => CreateOrchestrationSchemaDdl,
            IdentityDatabase.Schema => CreateIdentitySchemaDdl,
            ProjectsDatabase.Schema => CreateProjectsSchemaDdl,
            MemoryDatabase.Schema => CreateMemorySchemaDdl,
            ChatDatabase.Schema => CreateChatSchemaDdl,
            IntakeDatabase.Schema => CreateIntakeSchemaDdl,
            CostsDatabase.Schema => CreateCostsSchemaDdl,
            _ => throw new ArgumentException($"unknown schema: {schema}", nameof(schema)),
        };
#pragma warning restore CA2100

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string CreateOrchestrationSchemaDdl = "CREATE SCHEMA IF NOT EXISTS orchestration";
    private const string CreateIdentitySchemaDdl = "CREATE SCHEMA IF NOT EXISTS identity";
    private const string CreateProjectsSchemaDdl = "CREATE SCHEMA IF NOT EXISTS projects";
    private const string CreateMemorySchemaDdl = "CREATE SCHEMA IF NOT EXISTS memory";
    private const string CreateChatSchemaDdl = "CREATE SCHEMA IF NOT EXISTS chat";
    private const string CreateIntakeSchemaDdl = "CREATE SCHEMA IF NOT EXISTS intake";
    private const string CreateCostsSchemaDdl = "CREATE SCHEMA IF NOT EXISTS costs";
}
