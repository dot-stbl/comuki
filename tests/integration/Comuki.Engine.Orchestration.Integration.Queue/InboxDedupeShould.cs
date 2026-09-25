using Comuki.Engine.Orchestration.Domain.Inbox;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Inbox;
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
/// Inbox dedupe race against a real Postgres: <see cref="IInbox.TryClaimAsync"/>
/// is the only entry point that resolves concurrent double-claims to
/// exactly one winner. The <c>INSERT ... ON CONFLICT (message_id) DO
/// NOTHING</c> guard is what makes the PK uniqueness constraint
/// race-safe — both the sequential and the racing paths are covered here.
/// </summary>
/// <param name="postgres">The collection's shared Postgres (<see cref="QueueIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(QueueIntegrationCollection))]
public sealed class InboxDedupeShould(PostgresCollectionFixture postgres)
{
    private static readonly DateTimeOffset baseTime = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a self-contained provider; the inbox has no per-test transport to swap, so the defaults are constant.</summary>
    private async Task<ServiceProvider> BuildProviderAsync()
    {
        await postgres.ResetDatabaseAsync();

        var clock = new FakeTimeProvider(baseTime);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:Outbox:DispatchInterval"] = "01:00:00",
                ["Orchestration:Outbox:BatchSize"] = "25",
                ["Orchestration:Outbox:MaxAttempts"] = "5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddOrchestrationPersistence(postgres.ConnectionString);
        services.AddOrchestrationQueue(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>Re-reads every <c>inbox_receipts</c> row from a fresh scope (no tracking).</summary>
    /// <param name="provider">The DI container built by <see cref="BuildProviderAsync"/> — a fresh scope reads the row so no tracked entity leaks between assertions.</param>
    private static async Task<List<InboxReceipt>> LoadReceiptsAsync(IServiceProvider provider)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        return await db.InboxReceipts.AsNoTracking().ToListAsync(cancellationToken);
    }

    [Fact(DisplayName = "Given an already-claimed message id, when TryClaimAsync is called again, then it returns false and the existing row stays unique")]
    public async Task SecondClaimOfTheSameMessageIdIsRejectedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await BuildProviderAsync();

        using (var scope = provider.CreateScope())
        {
            var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

            var first = await inbox.TryClaimAsync("admission-42", cancellationToken);
            var second = await inbox.TryClaimAsync("admission-42", cancellationToken);

            first.ShouldBeTrue();
            second.ShouldBeFalse();
        }

        var receipts = await LoadReceiptsAsync(provider);
        receipts.ShouldHaveSingleItem();
        receipts[0].MessageId.ShouldBe("admission-42");
    }

    [Fact(DisplayName = "Given two concurrent claims for the same message id, when both TryClaimAsync run in parallel, then exactly one returns true")]
    public async Task ConcurrentDoubleClaimOfTheSameMessageIdResolvesToExactlyOneWinnerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await BuildProviderAsync();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var inboxA = scopeA.ServiceProvider.GetRequiredService<IInbox>();
        var inboxB = scopeB.ServiceProvider.GetRequiredService<IInbox>();

        var claimA = inboxA.TryClaimAsync("admission-99", cancellationToken);
        var claimB = inboxB.TryClaimAsync("admission-99", cancellationToken);
        var results = await Task.WhenAll(claimA, claimB);

        // Exactly one true, one false. A naive SELECT-then-INSERT
        // implementation would let both return true under the race.
        results.Count(static result => result).ShouldBe(1);
        results.Count(static result => !result).ShouldBe(1);

        var receipts = await LoadReceiptsAsync(provider);
        receipts.ShouldHaveSingleItem();
        receipts[0].MessageId.ShouldBe("admission-99");
    }
}
