using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// Proves the BackgroundService loop around <see cref="MemorySweepWorker"/>:
/// the direct <see cref="MemorySweepWorker.SweepOnceAsync"/> path, that
/// <see cref="BackgroundService.StopAsync"/> exits cleanly when the
/// stopping token fires, and that the loop runs at least one iteration
/// before cancellation. The first sweep runs immediately on entry to the
/// loop (the 1-hour <c>Task.Delay</c> comes after it), so the iteration
/// floor is observable without mocking time.
/// </summary>
public sealed class MemorySweepWorkerShould : IAsyncLifetime
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
        services.AddMemoryPersistence(container.GetConnectionString());
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

    [Fact(DisplayName = "Given N expired ephemeral facts, when SweepOnceAsync runs, then the expired rows are deleted and standing facts survive")]
    public async Task DeleteAllExpiredFactsAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var worker = MemorySweepWorkerShouldHelpers.NewWorker(store);
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var index = 0; index < 3; index++)
        {
            await store.WriteAsync(
                MemorySweepWorkerShouldHelpers.Write($"expired-{index}", $"ephemeral note {index}", MemoryFactKind.Ephemeral),
                cancellationToken);
        }
        await store.WriteAsync(
            MemorySweepWorkerShouldHelpers.Write("standing", "long decision", MemoryFactKind.Standing),
            cancellationToken);
        await MemorySweepWorkerShouldHelpers.ExecuteNonQueryAsync(
            provider,
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '15 days' "
            + "WHERE topic_key LIKE 'expired-%'",
            cancellationToken);

        await worker.SweepOnceAsync(cancellationToken);

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.ShouldHaveSingleItem().TopicKey.ShouldBe("standing");
    }

    [Fact(DisplayName = "Given no expired facts, when SweepOnceAsync runs, then every row survives (zero deleted)")]
    public async Task ReportZeroWhenNothingExpiredAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var worker = MemorySweepWorkerShouldHelpers.NewWorker(store);
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(
            MemorySweepWorkerShouldHelpers.Write("fresh-ephemeral", "fresh ephemeral", MemoryFactKind.Ephemeral),
            cancellationToken);
        await store.WriteAsync(
            MemorySweepWorkerShouldHelpers.Write("fresh-standing", "fresh standing", MemoryFactKind.Standing),
            cancellationToken);

        await worker.SweepOnceAsync(cancellationToken);

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.Select(static fact => fact.TopicKey).ShouldBe(["fresh-standing", "fresh-ephemeral"]);
    }

    [Fact(DisplayName = "Given ExecuteAsync is running, when the stopping token fires, then StopAsync exits cleanly within the deadline")]
    public async Task ExitCleanlyOnCancellationAsync()
    {
        var worker = MemorySweepWorkerShouldHelpers.NewWorker(provider.GetRequiredService<IMemoryStore>());
        var cancellationToken = TestContext.Current.CancellationToken;

        await worker.StartAsync(CancellationToken.None);
        // give ExecuteAsync a moment to enter the first sweep
        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(stopCts.Token);
    }

    [Fact(DisplayName = "Given expired facts and a running loop, when the stopping token fires after the first iteration, then at least one sweep has run")]
    public async Task IterateAtLeastOnceAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var worker = MemorySweepWorkerShouldHelpers.NewWorker(store);
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var index = 0; index < 2; index++)
        {
            await store.WriteAsync(
                MemorySweepWorkerShouldHelpers.Write($"old-{index}", $"ephemeral {index}", MemoryFactKind.Ephemeral),
                cancellationToken);
        }
        await MemorySweepWorkerShouldHelpers.ExecuteNonQueryAsync(
            provider,
            $"UPDATE {MemoryDatabase.Schema}.memory_facts SET created_at = created_at - interval '15 days' "
            + "WHERE topic_key LIKE 'old-%'",
            cancellationToken);

        await worker.StartAsync(CancellationToken.None);
        // the first sweep runs immediately on entry to the loop — give the
        // DB a generous window before we observe; the 1-hour Task.Delay
        // between iterations has not started yet
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: cancellationToken);
        visible.ShouldBeEmpty();

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StopAsync(stopCts.Token);
    }
}

/// <summary>File-scoped helpers: one public type per file keeps this scoped to <c>MemorySweepWorkerShould.cs</c>.</summary>
file static class MemorySweepWorkerShouldHelpers
{
    /// <summary>Constructs the worker with the deterministic clock the suite already uses.</summary>
    public static MemorySweepWorker NewWorker(IMemoryStore store)
    {
        return new MemorySweepWorker(store, SweepFixedTime.Provider, NullLogger<MemorySweepWorker>.Instance);
    }

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

    public static DateTimeOffset Now => Provider.GetUtcNow();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
