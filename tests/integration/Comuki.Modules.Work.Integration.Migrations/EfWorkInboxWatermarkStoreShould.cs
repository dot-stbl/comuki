using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Persistence;
using Comuki.Modules.Work.Infrastructure.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Work.Integration.Migrations;

/// <summary>
/// Round-trip proof for <see cref="EfWorkInboxWatermarkStore"/> —
/// the per-cycle scope + open-connection ref-counted pattern (the
/// structural fix for the watermark-standing-still bug, see
/// <see cref="EfWorkInboxWatermarkStore"/>). The first cycle
/// resolves a fresh <see cref="WorkDbContext"/> (the connection is
/// closed between scopes), <c>GetAsync</c> returns
/// <see cref="Guid.Empty"/>, then <c>RecordAsync</c> writes the
/// highest-seen id through an opened connection and a single SQL
/// upsert; the second cycle resolves a fresh scope again, and
/// <c>GetAsync</c> reads the row back. Without the
/// <c>OpenConnectionAsync</c>/<c>CloseConnectionAsync</c> pair, the
/// raw ADO call would run on a closed connection and throw at
/// <c>ExecuteNonQueryAsync</c> — that is the regression this
/// fixture catches. Counterpart of the unit-level in-memory
/// fixture; integration-side because the InMemory provider does
/// not exercise Npgsql's open/close lifecycle.
/// </summary>
public sealed class EfWorkInboxWatermarkStoreShould : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(PostgresImage)
        .Build();

    private string connectionString = string.Empty;
    private ServiceProvider services = null!;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);
        connectionString = container.GetConnectionString();

        // Wire WorkDbContext the same way AddWorkPersistence does so the
        // migrator walks the same migration graph and the test exercises
        // the real on-disk shape.
        var sc = new ServiceCollection();
        sc.AddDbContext<WorkDbContext>(
            (sp, builder) => WorkDbContext.ApplyOptions(builder, connectionString),
            optionsLifetime: ServiceLifetime.Singleton,
            contextLifetime: ServiceLifetime.Scoped);
        sc.AddSingleton(TimeProvider.System);
        sc.AddScoped<IWorkInboxWatermarkStore, EfWorkInboxWatermarkStore>();
        services = sc.BuildServiceProvider();

        await Shared.Migrations.ComukiDatabaseMigrator.EnsureAllAsync(connectionString, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given a fresh watermark row, when RecordAsync is called from a per-cycle scope, then GetAsync from the next scope reads the row back")]
    public async Task RecordAndReadRoundTripAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The struct-equal Guid.Empty separates "first cycle" from
        // "rows already processed"; the production dispatcher feeds
        // it through untouched on a cold start.
        var latest = Guid.Parse("01900000-0000-7000-8000-000000000001");

        // First cycle — resolve + write. The Id is monotonically
        // increasing so the SQL upsert's GREATEST() never moves the
        // row backward on a re-processing retry; the
        // Open/Close pair exercises the connection lifecycle that
        // unit InMemory does not.
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkInboxWatermarkStore>();
            await store.RecordAsync("work.task.attempt-requested.v1", latest, cancellationToken);
        }

        // Second cycle — resolve a fresh scope + read the watermark
        // back. The ReadAsync round-trip on this path was the
        // regression: the EF context's connection is closed between
        // scopes, and the raw ADO upsert needs the OpenConnection /
        // CloseConnection pairing to succeed (the predecessor code
        // threw at ExecuteNonQueryAsync with a closed connection —
        // see the per-task dual-write note in
        // EfWorkInboxWatermarkStore).
        Guid read;
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkInboxWatermarkStore>();
            read = await store.GetAsync("work.task.attempt-requested.v1", cancellationToken);
        }

        read.ShouldBe(latest);
    }

    [Fact(DisplayName = "Given a watermark row at id X, when a smaller id X' is recorded, then GetAsync still returns X (monotonic direction holds server-side)")]
    public async Task SmallerRecordDoesNotMoveWatermarkBackwardAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var larger = Guid.Parse("01900000-0000-7000-8000-0000000000FF");
        var smaller = Guid.Parse("01900000-0000-7000-8000-000000000001");

        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkInboxWatermarkStore>();
            await store.RecordAsync("orchestration.run.terminated.v1", larger, cancellationToken);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkInboxWatermarkStore>();
            await store.RecordAsync("orchestration.run.terminated.v1", smaller, cancellationToken);
        }

        Guid read;
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkInboxWatermarkStore>();
            read = await store.GetAsync("orchestration.run.terminated.v1", cancellationToken);
        }

        read.ShouldBe(larger);
    }
}
