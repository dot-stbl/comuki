using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.OutboxDispatch;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Testing.Clocks;
using Comuki.Host.Testing.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// Outbox dispatcher lifecycle against a real Postgres: the
/// <c>FOR UPDATE SKIP LOCKED</c> claim race (two concurrent dispatchers
/// split the backlog with no double-publish), the bounded retry /
/// dead-letter policy, and the "commit landed before dispatch ran"
/// recovery path. Self-contained service container — does not inherit
/// <c>QueueDatabase</c> because that base class's <c>InitializeAsync</c>
/// wires no per-test publisher override, and the dispatcher needs a
/// different <see cref="IOutboxPublisher"/> per scenario (counting,
/// always-throwing, noop). Direct <see cref="PostgresCollectionFixture"/>
/// primary-constructor parameter plus the
/// <c>[Collection(nameof(QueueIntegrationCollection))]</c> attribute —
/// the "Direct" shape <see cref="PostgresCollectionFixture"/>'s own
/// summary documents.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="QueueIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(QueueIntegrationCollection))]
public sealed class OutboxDispatchShould(PostgresCollectionFixture postgres)
{
    private static readonly DateTimeOffset baseTime = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a self-contained provider: the shared Postgres + the orchestration installers + the supplied publisher (last registration wins for IOutboxPublisher resolution).</summary>
    /// <param name="publisher">The <see cref="IOutboxPublisher"/> this scenario wires — replaces the Noop default (last registration wins).</param>
    /// <param name="batchSize">Bound to <c>Orchestration:Outbox:BatchSize</c> — the dispatcher's per-sweep claim limit.</param>
    /// <param name="maxAttempts">Bound to <c>Orchestration:Outbox:MaxAttempts</c> — the bounded retry budget before dead-lettering.</param>
    private async Task<(FakeTimeProvider clock, ServiceProvider provider)> BuildProviderAsync(
        IOutboxPublisher publisher,
        int batchSize = 25,
        int maxAttempts = 5)
    {
        await postgres.ResetDatabaseAsync();

        var clock = new FakeTimeProvider(baseTime);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:Outbox:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Orchestration:Outbox:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Orchestration:Outbox:DispatchInterval"] = "01:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddOrchestrationPersistence(postgres.ConnectionString);
        services.AddOrchestrationQueue(configuration);
        // AFTER AddOrchestrationQueue: the Noop installed by the
        // extension's TryAddScoped is replaced here (last registration
        // wins for the singular resolution the dispatcher does).
        services.AddSingleton(publisher);

        return (clock, services.BuildServiceProvider());
    }

    /// <summary>Seeds the supplied number of distinct outbox rows.</summary>
    /// <param name="provider">The DI container built by <see cref="BuildProviderAsync"/>.</param>
    /// <param name="clock">Time source for each seeded row's commit-order timestamp.</param>
    /// <param name="count">How many distinct outbox rows to seed.</param>
    private static async Task<List<Guid>> SeedOutboxAsync(IServiceProvider provider, TimeProvider clock, int count)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var seeded = new List<Guid>(count);
        var seedTime = clock.GetUtcNow();
        for (var i = 0; i < count; i++)
        {
            var message = OutboxMessage.Create(
                "orchestration.run.terminated.v1",
                /*lang=json,strict*/ $$"""{"i":{{i}}}""",
                seedTime.AddMilliseconds(i));
            db.OutboxMessages.Add(message);
            seeded.Add(message.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return seeded;
    }

    /// <summary>Re-reads one outbox row from a fresh scope (no tracking).</summary>
    /// <param name="provider">The DI container built by <see cref="BuildProviderAsync"/>.</param>
    /// <param name="id">The outbox row's id.</param>
    private static async Task<OutboxMessage?> LoadMessageAsync(IServiceProvider provider, Guid id)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(message => message.Id == id, cancellationToken);
    }

    [Fact(DisplayName = "Given a backlog bigger than the batch, when two dispatchers race, then they claim disjoint slices and publish every row exactly once")]
    public async Task TwoConcurrentDispatchersSplitTheBacklogWithNoDoubleDispatchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var countingPublisher = new CountingPublisher();
        var (_, provider) = await BuildProviderAsync(countingPublisher);
        await using (provider)
        {
            var seededIds = await SeedOutboxAsync(provider, /* clock */ new FakeTimeProvider(baseTime), 40);
            seededIds.Count.ShouldBe(40);

            using var scopeA = provider.CreateScope();
            using var scopeB = provider.CreateScope();
            var dispatcherA = scopeA.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            var dispatcherB = scopeB.ServiceProvider.GetRequiredService<OutboxDispatcher>();

            var taskA = dispatcherA.DispatchAsync(cancellationToken);
            var taskB = dispatcherB.DispatchAsync(cancellationToken);
            var results = await Task.WhenAll(taskA, taskB);

            // Whichever scope ran first hits the configured BatchSize=25;
            // the other scope mops up the remaining 15. Both call back
            // into the SAME counting publisher instance, so the total
            // invocation count is the source of truth for "no row was
            // handed to the publisher twice."
            results.Sum(static result => result.Dispatched).ShouldBe(40);
            results.Sum(static result => result.DeadLettered).ShouldBe(0);
            countingPublisher.InvocationCount.ShouldBe(40);

            // Every seeded row is now dispatched, none dead-lettered,
            // and no row saw a single failure attempt.
            foreach (var id in seededIds)
            {
                var message = (await LoadMessageAsync(provider, id)).ShouldNotBeNull();
                message.IsDispatched.ShouldBeTrue();
                message.IsDeadLettered.ShouldBeFalse();
                message.Attempts.ShouldBe(0);
            }
        }
    }

    [Fact(DisplayName = "Given one poisoned message and MaxAttempts=2, when the dispatcher sweeps three times, then the message dead-letters and stops being retried")]
    public async Task PoisonMessageDeadLettersAfterBoundedRetriesAndStaysVisibleAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var throwingPublisher = new ThrowingPublisher();
        var (_, provider) = await BuildProviderAsync(throwingPublisher, batchSize: 10, maxAttempts: 2);
        await using (provider)
        {
            var seededIds = await SeedOutboxAsync(provider, new FakeTimeProvider(baseTime), 1);
            var messageId = seededIds.ShouldHaveSingleItem();

            using var scope = provider.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

            // Sweep 1: one failure, Attempts becomes 1, not yet dead-lettered.
            await dispatcher.DispatchAsync(cancellationToken);

            var afterSweep1 = (await LoadMessageAsync(provider, messageId)).ShouldNotBeNull();
            afterSweep1.Attempts.ShouldBe(1);
            afterSweep1.DispatchedAt.ShouldBeNull();
            afterSweep1.LastError.ShouldNotBeNullOrWhiteSpace();
            afterSweep1.LastError!.ShouldContain("poison message");
            afterSweep1.DeadLetteredAt.ShouldBeNull();
            afterSweep1.IsDeadLettered.ShouldBeFalse();
            throwingPublisher.InvocationCount.ShouldBe(1);

            // Sweep 2: another failure, Attempts becomes 2 == MaxAttempts,
            // the row is dead-lettered.
            await dispatcher.DispatchAsync(cancellationToken);

            var afterSweep2 = (await LoadMessageAsync(provider, messageId)).ShouldNotBeNull();
            afterSweep2.Attempts.ShouldBe(2);
            afterSweep2.IsDeadLettered.ShouldBeTrue();
            afterSweep2.DeadLetteredAt.ShouldNotBeNull();
            throwingPublisher.InvocationCount.ShouldBe(2);

            // Sweep 3: the dead-lettered row is excluded from the claim's
            // WHERE ... dead_lettered_at IS NULL filter — the dispatcher
            // hits zero pending rows, so the publisher is never invoked.
            var (dispatched, deadLettered) = await dispatcher.DispatchAsync(cancellationToken);
            dispatched.ShouldBe(0);
            deadLettered.ShouldBe(0);
            throwingPublisher.InvocationCount.ShouldBe(2);

            // The dead-lettered row is still present — never silently dropped.
            var persisted = (await LoadMessageAsync(provider, messageId)).ShouldNotBeNull();
            persisted.IsDeadLettered.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Given a row committed before any dispatcher runs, when a fresh dispatcher cycle picks it up, then it is delivered")]
    public async Task SurvivesACrashBetweenCommitAndDispatchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var countingPublisher = new CountingPublisher();
        var (_, provider) = await BuildProviderAsync(countingPublisher);
        await using (provider)
        {
            // Seed in one scope, then dispose it WITHOUT dispatching
            // (simulating the commit landing and the process then crashing
            // before any dispatcher cycle).
            var seededIds = await SeedOutboxAsync(provider, new FakeTimeProvider(baseTime), 1);
            var messageId = seededIds.ShouldHaveSingleItem();

            countingPublisher.InvocationCount.ShouldBe(0);

            // In a brand-new scope (mimicking process restart + a later
            // dispatcher cycle) resolve a fresh OutboxDispatcher and run
            // one sweep. The seeded row lands cleanly.
            using (var freshScope = provider.CreateScope())
            {
                var dispatcher = freshScope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                var (dispatched, deadLettered) = await dispatcher.DispatchAsync(cancellationToken);
                dispatched.ShouldBe(1);
                deadLettered.ShouldBe(0);
            }

            countingPublisher.InvocationCount.ShouldBe(1);
            var recovered = (await LoadMessageAsync(provider, messageId)).ShouldNotBeNull();
            recovered.IsDispatched.ShouldBeTrue();
            recovered.DispatchedAt.ShouldNotBeNull();
        }
    }

    /// <summary>Happy-path publisher that counts every delivery; never throws.</summary>
    private sealed class CountingPublisher : IOutboxPublisher
    {
        public int InvocationCount;

        public Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref InvocationCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>Publisher that always throws — exercises the bounded retry / dead-letter branch.</summary>
    private sealed class ThrowingPublisher : IOutboxPublisher
    {
        public int InvocationCount;

        public Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref InvocationCount);
            throw new InvalidOperationException("poison message — always fails");
        }
    }
}
