using Comuki.Shared.Migrations.Targets;
using Npgsql;

namespace Comuki.Shared.Migrations;

/// <summary>
/// Boot-time migration entry point: applies every module's pending EF Core
/// migrations under one session-level <c>pg_advisory_lock</c>, so a host
/// replica starting next to the standalone <c>comuki-migrator</c> (or
/// another replica) waits its turn instead of racing the same DDL — the
/// second caller acquires the lock and then sees zero pending migrations.
/// </summary>
public static class ComukiDatabaseMigrator
{
    /// <summary>
    /// Advisory-lock key. A fixed 63-bit constant keyed to this project —
    /// any literal works as long as every caller of
    /// <see cref="EnsureAllAsync"/> and the standalone migrator share it.
    /// </summary>
    private const long AdvisoryLockKey = 6_349_762_201_409_117L;

    private const string AcquireLockSql = "SELECT pg_advisory_lock(@key)";
    private const string ReleaseLockSql = "SELECT pg_advisory_unlock(@key)";

    /// <summary>
    /// Ensures every module schema exists and applies every pending
    /// migration, one context at a time, under the advisory lock. EF
    /// migrations are transactional and idempotent, so a database that is
    /// already current is a no-op that returns an empty summary.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task<DatabaseMigrationSummary> EnsureAllAsync(string connectionString, CancellationToken cancellationToken)
    {
        // The lock lives on its own session: the connection is held open
        // for the whole pass and the finally-block unlock keeps the pair
        // balanced even when a migration throws (closing the session would
        // also release it, but an explicit unlock keeps the intent visible).
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandText = AcquireLockSql;
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var applied = new List<DatabaseMigrationSummary.SchemaMigrationsApplied>();

            foreach (var target in MigrationTargets.All)
            {
                var migrations = await target.ApplyAsync(connectionString, cancellationToken);
                if (migrations.Count > 0)
                {
                    applied.Add(new DatabaseMigrationSummary.SchemaMigrationsApplied(target.Label, migrations));
                }
            }

            return new DatabaseMigrationSummary { AppliedSchemas = applied };
        }
        finally
        {
            // Cancellation must not skip the unlock — the session is about
            // to close anyway, and CancellationToken.None keeps it best-effort.
            await using var unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = ReleaseLockSql;
            unlockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
