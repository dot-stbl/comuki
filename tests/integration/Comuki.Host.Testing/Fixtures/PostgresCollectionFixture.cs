using Comuki.Shared.Migrations.Targets;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Testing.Fixtures;

/// <summary>
/// One <c>pgvector/pgvector:pg16</c> Postgres container, migrated with
/// every module's schema exactly once per test-run process
/// (<see cref="HostDatabaseMigrator.MigrateAllAsync"/>), shared by every
/// test class in a project's xUnit collection. Two integration shapes
/// are supported; the project's collection shape picks which one to use.
/// <list type="number">
/// <item>
/// <b>Direct — the test class takes the fixture.</b> Test classes that
/// touch Postgres take <see cref="PostgresCollectionFixture"/> as a
/// primary-constructor parameter; the project's
/// <c>[CollectionDefinition]</c> declares
/// <c>ICollectionFixture&lt;PostgresCollectionFixture&gt;</c>. xUnit v3
/// injects the collection fixture into the test class's own constructor
/// directly. See
/// <c>Comuki.Host.Integration.Costs.CostsIntegrationCollection</c>
/// (used by <c>OrchestrationBudgetGateShould</c> and
/// <c>PlatformCostsEndpointShould</c>) and
/// <c>Comuki.Engine.Orchestration.Integration.Queue.QueueIntegrationCollection</c>
/// (used by <c>QueueDatabase</c>) for the pattern.
/// </item>
/// <item>
/// <b>Containment — a host-server fixture owns the fixture.</b> When a
/// project also needs a project-specific host server (e.g.
/// <c>Comuki.Host.Integration.Auth.HostAuthServer</c>,
/// <c>Comuki.Host.Integration.Oidc.HostOidcServer</c>,
/// <c>Comuki.Host.Integration.Smoke.SmokeHostServer</c>) and that host
/// server is itself the thing shared via
/// <c>ICollectionFixture&lt;THostServer&gt;</c>, the host-server class
/// owns a <see cref="PostgresCollectionFixture"/> as a plain field and
/// calls its <see cref="InitializeAsync"/> / <see cref="DisposeAsync"/>
/// itself. Declaring
/// <c>ICollectionFixture&lt;PostgresCollectionFixture&gt;</c> as a
/// SECOND entry on the same <c>[CollectionDefinition]</c> does NOT work:
/// xUnit v3's collection-fixture resolver does not inject one declared
/// <c>ICollectionFixture&lt;T&gt;</c> into another declared collection
/// fixture's constructor (verified empirically — every test fails with
/// "unresolved constructor arguments" at run time when this is tried).
/// Containment is the supported escape. See
/// <c>Comuki.Host.Integration.Auth.AuthIntegrationCollection</c>,
/// <c>Comuki.Host.Integration.Oidc.OidcIntegrationCollection</c>, and
/// <c>Comuki.Host.Integration.Smoke.SmokeIntegrationCollection</c> for
/// the pattern.
/// </item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Replaces one container per test class with one container per
/// project.</b> Before this fixture existed, 19 of 21 host integration
/// projects each built their own <see cref="PostgreSqlContainer"/> per
/// test class (some per test method), paying the ~10-50s cold-start +
/// migrate cost every time (testing-audit-report.md §4.1). This fixture
/// pays that cost once; <see cref="ResetDatabaseAsync"/> truncates every
/// migrated table back to empty in ~100ms via <c>Respawn</c> instead.
/// </para>
/// <para>
/// <b>Reset is a DB-level concern only.</b> A project whose host fixture
/// seeds process-lifetime state into the database at boot (comuki's own
/// bootstrap admin account, seeded once by <c>BootstrapAdminComukiWorker</c>
/// when the host starts) must re-seed that state itself after a reset —
/// this type has no knowledge of any particular host composition. See
/// <c>Comuki.Host.Integration.Costs.PlatformCostsEndpointShould.InitializeAsync</c>
/// for the pattern: reset the database, then boot (or reboot) the host so
/// its own startup seeding runs again against the now-empty tables. Auth
/// and Intake never call <see cref="ResetDatabaseAsync"/> at all — both
/// already shared one host/database across their whole suite before WS2,
/// with no per-test reset, and that conversion intentionally preserved it.
/// </para>
/// <para>
/// <b><c>WithReuse(true)</c> is opt-in-safe by construction.</b> This
/// fixture only calls it when
/// the caller has already set Testcontainers' own
/// <c>TESTCONTAINERS_REUSE_ENABLE=true</c> — the library's documented
/// double opt-in, so a default run (CI, or a dev machine that never set
/// that variable) always gets a fresh container that <see
/// cref="DisposeAsync"/> tears down deterministically. No test may depend
/// on reuse to pass. Because this repo's local Podman runtime disables
/// Ryuk (<c>TESTCONTAINERS_RYUK_DISABLED=true</c> — Ryuk's reaper
/// container fights rootless Podman on Windows), a reused container is
/// never auto-reaped between separate <c>dotnet run</c> invocations even
/// when reuse is on; clean one up manually with (PowerShell):
/// <c>podman ps -a --filter "label=comuki.test-fixture=postgres-collection" -q | ForEach-Object { podman rm -f $_ }</c>.
/// A non-reused container is always stopped and removed by this type's own
/// <see cref="DisposeAsync"/> on a clean process exit; only an abnormal
/// kill leaks it, the same exposure Ryuk-disabled has for every fixture in
/// this project, reuse or not.
/// </para>
/// </remarks>
public sealed class PostgresCollectionFixture : IAsyncLifetime
{
    /// <summary>Testcontainers' own double opt-in for container reuse — this fixture never enables reuse unless the caller has also set this.</summary>
    private const string ReuseEnabledEnvVar = "TESTCONTAINERS_REUSE_ENABLE";

    /// <summary>Label applied only when reuse is on — the filter the cleanup command in this type's remarks matches on.</summary>
    private const string ReuseLabelName = "comuki.test-fixture";
    private const string ReuseLabelValue = "postgres-collection";

    /// <summary>EF Core's per-schema migrations-history table name (see every <c>&lt;Module&gt;DbContext.OnConfiguring</c>'s <c>MigrationsHistoryTable</c> call) — Respawn must never truncate it, or the next process's migrate pass thinks nothing is applied.</summary>
    private const string MigrationsHistoryTableName = "__ef_migrations_history";

    private static bool ReuseRequested =>
        string.Equals(Environment.GetEnvironmentVariable(ReuseEnabledEnvVar), "true", StringComparison.OrdinalIgnoreCase);

    private readonly PostgreSqlContainer container;
    private NpgsqlConnection connection = null!;
    private Respawner respawner = null!;

    public PostgresCollectionFixture()
    {
        var builder = new PostgreSqlBuilder("pgvector/pgvector:pg16");
        if (ReuseRequested)
        {
            builder = builder.WithReuse(true).WithLabel(ReuseLabelName, ReuseLabelValue);
        }

        container = builder.Build();
    }

    /// <summary>The migrated container's connection string. Valid only after <see cref="InitializeAsync"/> completes.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        ConnectionString = container.GetConnectionString();
        await HostDatabaseMigrator.MigrateAllAsync(ConnectionString, cancellationToken);

        connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [.. MigrationTargets.All.Select(static target => target.Schema)],
            TablesToIgnore = [.. MigrationTargets.All.Select(static target => new Table(target.Schema, MigrationsHistoryTableName))],
        });
    }

    /// <summary>
    /// Truncates every migrated table back to empty (Respawn, ~100ms for
    /// this schema set). Call from a per-test or per-class setup that needs
    /// a clean slate — never from a shared collection fixture's own
    /// <c>InitializeAsync</c>, which only runs once for the whole
    /// collection, not once per test.
    /// </summary>
    public Task ResetDatabaseAsync()
    {
        return respawner.ResetAsync(connection);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await container.DisposeAsync();
    }
}
