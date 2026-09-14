using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Migrator.Sources;
using Comuki.Migrator.Status;
using Comuki.Shared.Bootstrap.Cli;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Migrations;
using Comuki.Shared.Migrations.Targets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Operator CLI (issue #56): `comuki-migrator version` runs before any
// bootstrap; `comuki-migrator status` is a dry-run over every schema.
if (ComukiCli.IsCommand(args, ComukiCli.VersionCommand))
{
    return ComukiCli.RunVersion("comuki-migrator");
}

Console.WriteLine(ComukiBuildInfo.Read().ToVersionLine("comuki-migrator"));

var statusRequested = args.Contains("status", StringComparer.Ordinal);
var recreate = args.Contains("--recreate", StringComparer.Ordinal);

var connectionString = ConnectionStringSource.TryResolve(out var fromLegacyAlias);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        $"connection string not found: set the {ConnectionStringSource.EnvVariable} env var "
        + "or connectionStrings.comuki in config.toml");
    return 1;
}

if (fromLegacyAlias)
{
    Console.Error.WriteLine(
        $"warning: connection string resolved from the legacy {ConnectionStringSource.LegacyEnvVariable} env var; "
        + $"rename it to {ConnectionStringSource.EnvVariable}");
}

if (statusRequested)
{
    return await MigratorStatusRunner.RunAsync(connectionString);
}

if (recreate)
{
    var dropOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
    OrchestrationDbContext.ApplyOptions(dropOptions, connectionString);
    await using var forDrop = new OrchestrationDbContext(dropOptions.Options);

    await forDrop.Database.EnsureDeletedAsync();
    Console.WriteLine("database dropped (--recreate)");
}

// The apply pass runs under the shared advisory lock — the host's
// boot-time auto-migrate and this exe serialise against each other.
var summary = await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, CancellationToken.None);

foreach (var schema in summary.AppliedSchemas)
{
    foreach (var migration in schema.Migrations)
    {
        Console.WriteLine($"applied ({schema.Schema}): {migration}");
    }
}

if (summary.TotalApplied == 0)
{
    Console.WriteLine("all schemas up to date");
}

return 0;

/// <summary>
/// The status dry-run (issue #56 §3): pending migrations per schema
/// without applying anything. Exit codes — 0 every schema up to date,
/// 1 at least one pending, 2 a probe error (per CI gates).
/// </summary>
file static class MigratorStatusRunner
{
    /// <summary>Probes every schema for pending migrations and returns the CI exit code.</summary>
    public static async Task<int> RunAsync(string connectionString)
    {
        var totalPending = 0;
        foreach (var target in MigrationTargets.All)
        {
            try
            {
                var status = new MigratorSchemaStatus(target.Label, await target.PendingAsync(connectionString, CancellationToken.None));
                totalPending += status.PendingMigrations.Count;
                foreach (var line in MigratorStatusReport.RenderLines(status))
                {
                    Console.WriteLine(line);
                }
            }
            // 42P01 = undefined_table, 3F000 = invalid_schema_name: the schema/history was never provisioned
            catch (PostgresException exception) when (exception.SqlState is "42P01" or "3F000")
            {
                // dry-run keeps zero DDL: a schema that was never provisioned reads as "everything pending"
                totalPending++;
                Console.WriteLine($"{target.Label}: schema not provisioned — run comuki-migrator to create and migrate");
            }
            catch (NpgsqlException exception)
            {
                Console.Error.WriteLine($"error ({target.Label}): {exception.Message}");
                return MigratorStatusReport.ErrorExitCode;
            }
        }

        return MigratorStatusReport.ExitCode(totalPending);
    }
}
