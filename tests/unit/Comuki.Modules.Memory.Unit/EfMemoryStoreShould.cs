using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Round-trip and forget semantics of <see cref="EfMemoryStore"/> over
/// the EF Core in-memory provider. The pgvector and Npgsql-specific paths
/// (<see cref="MemoryFactVectors"/>) are out of scope for the in-memory
/// provider; <see cref="EfMemoryStore.WriteAsync"/> uses
/// <c>ExecuteUpdateAsync</c> which the in-memory provider does not
/// implement — that surface is covered by the Testcontainers-Postgres
/// integration suite. The plain LINQ reads (ListAsync, ForgetAsync)
/// exercise the EF-level behavior.
/// </summary>
public sealed class EfMemoryStoreShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a manually seeded fact, when ListAsync is called, then the visible fact is returned with the right shape")]
    public async Task ListAsyncReturnsSeededFactAsync()
    {
        using var session = NewSession();
        await using (var seedDb = await session.OpenAsync())
        {
            await SeedAsync(seedDb, Fact("deploy", "prefers docker compose"));
        }
        var store = session.Store;

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
        visible[0].TopicKey.ShouldBe("deploy");
        visible[0].Text.ShouldBe("prefers docker compose");
        visible[0].SubjectId.ShouldBe("user-1");
        visible[0].Scope.ShouldBe(MemoryScope.User);
        visible[0].Kind.ShouldBe(MemoryFactKind.Standing);
    }

    [Fact(DisplayName = "Given a superseded fact and a fresh one, when ListAsync is called, then only the fresh one is returned")]
    public async Task ListAsyncExcludesSupersededFactsAsync()
    {
        using var session = NewSession();
        var older = Fact("deploy", "first-version");
        older.Supersede(now.AddMinutes(5));
        await using (var seedDb = await session.OpenAsync())
        {
            await SeedAsync(seedDb, older, Fact("deploy", "second-version"));
        }
        var store = session.Store;

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
        visible[0].Text.ShouldBe("second-version");
    }

    [Fact(DisplayName = "Given a seeded fact, when ForgetAsync is called, then ListAsync no longer returns it")]
    public async Task ForgetRemovesFactFromListAsync()
    {
        using var session = NewSession();
        var seeded = Fact("deploy", "prefers docker compose");
        await using (var seedDb = await session.OpenAsync())
        {
            await SeedAsync(seedDb, seeded);
        }
        var store = session.Store;

        var removed = await store.ForgetAsync(seeded.Id, TestContext.Current.CancellationToken);

        removed.ShouldBeTrue();
        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: TestContext.Current.CancellationToken);
        visible.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given an unknown fact id, when ForgetAsync is called, then it returns false without touching the store")]
    public async Task ForgetUnknownIdReturnsFalseAsync()
    {
        using var session = NewSession();
        var store = session.Store;
        var unknownId = new Domain.Ids.MemoryFactId(Guid.NewGuid());

        var removed = await store.ForgetAsync(unknownId, TestContext.Current.CancellationToken);

        removed.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given more than 100 facts, when ListAsync is called without arguments, then at most the default page is returned")]
    public async Task ListAsyncDefaultsToHundredFactsAsync()
    {
        using var session = NewSession();
        const int totalFacts = 105;
        var seeded = new MemoryFact[totalFacts];
        for (var index = 0; index < totalFacts; index++)
        {
            seeded[index] = Fact($"topic-{index:000}", $"text-{index:000}", now.AddSeconds(-index));
        }

        await using (var seedDb = await session.OpenAsync())
        {
            await SeedAsync(seedDb, seeded);
        }
        var store = session.Store;

        var visible = await store.ListAsync(MemoryScope.User, "user-1", cancellationToken: TestContext.Current.CancellationToken);

        // Performance audit (2026-09-09) §1.4: the unbounded read became
        // a hard page cap of IMemoryStore.DefaultListLimit (100).
        visible.Count.ShouldBe(IMemoryStore.DefaultListLimit);
    }

    [Fact(DisplayName = "Given more than one page of facts, when ListAsync is called with explicit limit and offset, then the requested slice is returned")]
    public async Task ListAsyncRespectsLimitAndOffsetAsync()
    {
        using var session = NewSession();
        const int totalFacts = 25;
        var seeded = new MemoryFact[totalFacts];
        for (var index = 0; index < totalFacts; index++)
        {
            // each fact's CreatedAt is one second apart, so the
            // freshness order is deterministic in EF Core InMemory.
            seeded[index] = Fact($"topic-{index:000}", $"text-{index:000}", now.AddSeconds(-index));
        }

        await using (var seedDb = await session.OpenAsync())
        {
            await SeedAsync(seedDb, seeded);
        }
        var store = session.Store;

        var firstPage = await store.ListAsync(
            MemoryScope.User, "user-1", limit: 10, offset: 0, TestContext.Current.CancellationToken);
        var secondPage = await store.ListAsync(
            MemoryScope.User, "user-1", limit: 10, offset: 10, TestContext.Current.CancellationToken);

        firstPage.Count.ShouldBe(10);
        secondPage.Count.ShouldBe(10);
        // Standing facts rank before ephemeral by freshness — and every
        // seeded fact here is Standing — so the freshness ordering is
        // the ranking. The two pages must not overlap.
        var firstIds = firstPage.Select(static fact => fact.Id).ToHashSet();
        secondPage.Any(fact => firstIds.Contains(fact.Id)).ShouldBeFalse();
    }

    private static MemoryFact Fact(string topicKey, string text, DateTimeOffset? createdAt = null)
    {
        return MemoryFact.Create(
            MemoryScope.User,
            "user-1",
            MemoryFactKind.Standing,
            topicKey,
            text,
            MemorySource.Chat,
            "user-1",
            createdAt ?? now);
    }

    private static async Task SeedAsync(MemoryDbContext db, params MemoryFact[] facts)
    {
        await db.MemoryFacts.AddRangeAsync(facts);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Owns the <see cref="MemoryDbContext"/> options and an <see cref="EfMemoryStore"/>
    /// over the same in-memory database for the lifetime of the test —
    /// the store's per-call context shares the options but each call
    /// creates a fresh context so EF Core's per-call lifecycle is
    /// respected.
    /// </summary>
    private static Session NewSession()
    {
        var databaseName = $"memory-store-tests-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(static warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new TestDbContextFactory(options);
        var store = new EfMemoryStore(factory, TimeProvider.System, NullLogger<EfMemoryStore>.Instance);
        return new Session(options, store);
    }

    private sealed class Session(DbContextOptions<MemoryDbContext> options, EfMemoryStore store) : IDisposable
    {
        public DbContextOptions<MemoryDbContext> Options { get; } = options;
        public EfMemoryStore Store { get; } = store;

        public async Task<MemoryDbContext> OpenAsync()
        {
            return await Task.FromResult(new MemoryDbContext(Options));
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<MemoryDbContext> options) : IDbContextFactory<MemoryDbContext>
    {
        private readonly DbContextOptions<MemoryDbContext> options = options;

        public MemoryDbContext CreateDbContext()
        {
            return new MemoryDbContext(options);
        }
    }
}
