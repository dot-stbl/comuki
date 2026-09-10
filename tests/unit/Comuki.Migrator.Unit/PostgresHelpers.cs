using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Intake.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Postgres-side helpers shared by the <c>DatabaseSchemaEnsurerShould</c>
/// test class: <c>[Theory]</c> row data for the ten module schemas,
/// and a reusable <c>information_schema.schemata</c> lookup used to
/// verify that <c>CREATE SCHEMA IF NOT EXISTS</c> actually landed.
/// </summary>
internal static class PostgresHelpers
{
    /// <summary>The ten module schemas the Migrator's switch statement dispatches.</summary>
    public static TheoryData<string> KnownSchemas()
    {
        var data = new TheoryData<string>();
        foreach (var schema in AllSchemas())
        {
            data.Add(schema);
        }
        return data;
    }

    /// <summary>The ten module schemas as a plain sequence (for <c>foreach</c> inside Facts).</summary>
    public static IEnumerable<string> AllSchemas()
    {
        yield return OrchestrationDatabase.Schema;
        yield return IdentityDatabase.Schema;
        yield return ProjectsDatabase.Schema;
        yield return MemoryDatabase.Schema;
        yield return ChatDatabase.Schema;
        yield return IntakeDatabase.Schema;
        yield return CostsDatabase.Schema;
        yield return KnowledgeDatabase.Schema;
        yield return ArtifactsDatabase.Schema;
        yield return SchedulerDatabase.Schema;
    }

    /// <summary>Read every row in <c>information_schema.schemata</c> for a given connection.</summary>
    public static async Task<IReadOnlyList<string>> QuerySchemasAsync(string connectionString, CancellationToken cancellationToken)
    {
        var schemas = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }
}
