using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// Proves the memory sweep as a comuki worker: the direct
/// <c>ExecuteAsync</c> path through a <see cref="WorkerContext"/>, and the
/// real <see cref="ComukiWorkerRegistry"/> loop over the module
/// registration (first cycle runs immediately, status snapshot reports
/// the worker healthy, shutdown stops cleanly). The 1-hour interval only
/// delays the SECOND cycle, so the iteration floor is observable without
/// mocking time.
/// <para>
/// The fixture registers the real <see cref="AsyncLocalSubjectScopeAccessor"/>
/// (a host-equivalent composition — the worker ctor requires it) and each
/// test establishes a system scope on that shared instance: the accessor's
/// <see cref="AsyncLocal{T}"/> state is per-instance, so the store's
/// context (built over the same registered singleton) sees it.
/// </para>
/// </summary>
public sealed class MemorySweepComukiWorkerShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(SweepFixedTime.Provider);
        services.AddSingleton<ISubjectScopeAccessor, AsyncLocalSubjectScopeAccessor>();
        services.AddMemoryPersistence(container.GetConnectionString());
        services.AddComukiWorkers();
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given N expired ephemeral facts, when the worker executes, then the expired rows are deleted and standing facts survive")]
    public async Task DeleteAllExpiredFactsAsync()
    {
        using var systemScope = NewSystemScope();
        var store = provider.GetRequiredService<IMemoryStore>();
        var worker = NewWorker();
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var index = 0; index < 3; index++)
        {
            await store.WriteAsync(
                MemorySweepComukiWorkerShouldHelpers.Write($"expired-{index}", $"ephemeral note {index}", MemoryFactKind.Ephemeral),
                cancellationToken);
        }
        await store.WriteAsync(
            MemorySweepComukiWorkerShouldHelpers.Write("standing", "long decision", MemoryFactKind.Standing),
            cancellationToken);
        await MemorySweepComukiWorkerShouldHelpers.ExecuteNonQueryAsync(
            provider,
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '15 days' "
            + "WHERE topic_key LIKE 'expired-%'",
            cancellationToken);

        var result = await ExecuteOnceAsync(worker, cancellationToken);

        result.Success.ShouldBeTrue();
        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.ShouldHaveSingleItem().TopicKey.ShouldBe("standing");
    }

    [Fact(DisplayName = "Given no expired facts, when the worker executes, then every row survives and the cycle reports zero swept")]
    public async Task ReportZeroWhenNothingExpiredAsync()
    {
        using var systemScope = NewSystemScope();
        var store = provider.GetRequiredService<IMemoryStore>();
        var worker = NewWorker();
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(
            MemorySweepComukiWorkerShouldHelpers.Write("fresh-ephemeral", "fresh ephemeral", MemoryFactKind.Ephemeral),
            cancellationToken);
        await store.WriteAsync(
            MemorySweepComukiWorkerShouldHelpers.Write("fresh-standing", "fresh standing", MemoryFactKind.Standing),
            cancellationToken);

        var result = await ExecuteOnceAsync(worker, cancellationToken);

        result.Success.ShouldBeTrue();
        result.Data.ShouldBe(0);
        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.Select(static fact => fact.TopicKey).ShouldBe(["fresh-standing", "fresh-ephemeral"]);
    }

    [Fact(DisplayName = "Given the registry is running, when the stopping token fires after the first cycle, then StopAsync exits cleanly and the status snapshot is healthy")]
    public async Task RegistryLoopSweepsAndStopsCleanlyAsync()
    {
        using var systemScope = NewSystemScope();
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var index = 0; index < 2; index++)
        {
            await store.WriteAsync(
                MemorySweepComukiWorkerShouldHelpers.Write($"old-{index}", $"ephemeral {index}", MemoryFactKind.Ephemeral),
                cancellationToken);
        }
        await MemorySweepComukiWorkerShouldHelpers.ExecuteNonQueryAsync(
            provider,
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '15 days' "
            + "WHERE topic_key LIKE 'old-%'",
            cancellationToken);

        var registry = provider.GetRequiredService<ComukiWorkerRegistry>();
        await registry.StartAsync(CancellationToken.None);
        // the first cycle runs immediately on entry to the loop — give the
        // DB a generous window before we observe; the 1-hour delay between
        // iterations has not started yet
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.ShouldBeEmpty();

        var status = registry.Snapshot().Single(static candidate => candidate.Name == "memory-sweep");
        status.LastRunAt.ShouldNotBeNull();
        status.LastResult.ShouldNotBeNull().Success.ShouldBeTrue();
        status.ConsecutiveFailures.ShouldBe(0);
        status.IsHealthy.ShouldBeTrue();

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await registry.StopAsync(stopCts.Token);
    }

    private IDisposable NewSystemScope()
    {
        return provider.GetRequiredService<ISubjectScopeAccessor>().AsSystem("sweep-test");
    }

    private MemorySweepComukiWorker NewWorker()
    {
        return new MemorySweepComukiWorker(
            provider.GetRequiredService<IMemoryStore>(),
            provider.GetRequiredService<ISubjectScopeAccessor>(),
            SweepFixedTime.Provider,
            NullLogger<MemorySweepComukiWorker>.Instance);
    }

    private static async Task<WorkerResult> ExecuteOnceAsync(MemorySweepComukiWorker worker, CancellationToken cancellationToken)
    {
        await using var scope = SweepFixedTime.NewScope();
        return await worker.ExecuteAsync(
            new WorkerContext(scope.ServiceProvider, SweepFixedTime.Provider, NullLogger.Instance),
            cancellationToken);
    }
}

/// <summary>File-scoped helpers: one public type per file keeps this scoped to <c>MemorySweepComukiWorkerShould.cs</c>.</summary>
file static class MemorySweepComukiWorkerShouldHelpers
{
    /// <summary>A single subject, fixed source — matches the existing migrations fixture shape.</summary>
    public static MemoryFactWrite Write(string topicKey, string text, MemoryFactKind kind = MemoryFactKind.Standing)
    {
        return new MemoryFactWrite(
            MemoryScope.User,
            "user-1",
            kind,
            topicKey,
            text,
            MemorySource.Chat,
            "user-1");
    }

    /// <summary>Runs one raw UPDATE through the same connection the store uses (the migration test does the same).</summary>
    public static async Task ExecuteNonQueryAsync(IServiceProvider services, string sql, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<MemoryDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Deterministic clock local to this file (file-static so it does not collide with <c>FixedTime</c> in the migration fixture).</summary>
file static class SweepFixedTime
{
    public static readonly TimeProvider Provider = new FixedTimeProvider();

    private static readonly ServiceProvider emptyProvider = new ServiceCollection().BuildServiceProvider();

    public static AsyncServiceScope NewScope()
    {
        return emptyProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
