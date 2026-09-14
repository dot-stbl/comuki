using Comuki.Shared.Migrations;

namespace Comuki.Host.Testing;

/// <summary>
/// Migrates every module's EF Core context against one Postgres connection
/// string — a thin delegation to <c>ComukiDatabaseMigrator.EnsureAllAsync</c>,
/// the same pass <c>Comuki.Migrator</c> applies before production boots and
/// the host's boot-time auto-migrate runs (same order, same one-context-per-
/// schema contract, same advisory lock).
/// </summary>
/// <remarks>
/// Before this type existed, each integration harness hand-listed which
/// contexts to migrate, and every one of them fell behind as the host grew
/// modules: <c>Host.Integration.Auth</c> migrated three of ten,
/// <c>Host.Integration.Intake</c> five of ten — both failed at runtime with
/// <c>42P01: relation "…" does not exist</c> the moment a hosted service
/// the host wires unconditionally (schedulers, sweepers, packagers) touched
/// a module's table the harness never migrated. The shared target list in
/// <c>Comuki.Shared.Migrations</c> is now the single place a new module's
/// context joins the migrated set — an eleventh module means one new entry
/// there, not an edit to every <c>Host*Server.cs</c> fixture in
/// <c>tests/integration/</c>.
/// </remarks>
public static class HostDatabaseMigrator
{
    /// <summary>Applies every module's pending migrations against <paramref name="connectionString"/>, one context at a time.</summary>
    /// <param name="connectionString">Postgres connection string (the Testcontainers instance the harness just started).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task MigrateAllAsync(string connectionString, CancellationToken cancellationToken)
    {
        await ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);
    }
}
