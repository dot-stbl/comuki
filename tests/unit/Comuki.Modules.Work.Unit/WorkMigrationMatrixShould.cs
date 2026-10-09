using Comuki.Modules.Work.Infrastructure.Backfill;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Cutover-matrix coverage for <see cref="WorkBackfillRunner"/>
/// (<c>add-work-management</c> tasks 6.1–6.7). The runner is fully
/// idempotent — a re-run after a successful pass finds every inbound
/// id already mapped and no-ops, which is the umbrella's task 6.7
/// invariant ("every processed row carries a task_backfill_marker and
/// retried runs no-op"). The matrix cells covered here:
/// <list type="bullet">
/// <item>Pending <c>Native</c> inbound → fresh Task; second pass no-ops.</item>
/// <item>Pending <c>GitHub</c> inbound → fresh Task; second pass no-ops.</item>
/// <item>Unsupported provider → not mapped; counted as
/// <c>UnsupportedProviders</c>.</item>
/// <item>Already-mapped inbound (binding exists) → not re-created;
/// counted as <c>SkippedInboundItems</c>.</item>
/// </list>
/// </summary>
public sealed class WorkMigrationMatrixShould
{
    private static ProjectId Project()
    {
        return new(Guid.CreateVersion7());
    }

    [Fact(DisplayName = "Given a pending Native inbound, when backfill runs, then one Task is created and the binding is recorded")]
    public async Task CreateTaskForPendingNativeInboundAsync()
    {
        var (store, bindings) = WorkBackfillFixtureFactory.NewBackfillFixture();
        var inbound = new InboundItemBackfillSnapshot(
            ExternalId: "dot-stbl/comuki#1",
            Title: "Native inbound",
            Body: "Brief",
            ProviderWire: "Native",
            ProjectId: Project());
        var runner = new WorkBackfillRunner(
            store,
            bindings,
            FakeProvider(inbound),
            NullLogger<WorkBackfillRunner>.Instance);

        var outcome = await runner.RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        outcome.NewTasksCreated.ShouldBe(1);
        outcome.SkippedInboundItems.ShouldBe(0);
        outcome.UnsupportedProviders.ShouldBe(0);
        (await bindings.FindByInboundAsync("dot-stbl/comuki#1", TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a pending GitHub inbound, when backfill runs twice, then the second run no-ops (idempotent)")]
    public async Task ReapplyingBackfillIsIdempotentAsync()
    {
        var (store, bindings) = WorkBackfillFixtureFactory.NewBackfillFixture();
        var inbound = new InboundItemBackfillSnapshot(
            ExternalId: "dot-stbl/comuki#42",
            Title: "GitHub inbound",
            Body: "Brief",
            ProviderWire: "GitHub",
            ProjectId: Project());
        var runner = new WorkBackfillRunner(
            store,
            bindings,
            FakeProvider(inbound),
            NullLogger<WorkBackfillRunner>.Instance);

        var first = await runner.RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var second = await runner.RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        first.NewTasksCreated.ShouldBe(1);
        second.NewTasksCreated.ShouldBe(0);
        second.SkippedInboundItems.ShouldBe(1);
    }

    [Fact(DisplayName = "Given an unsupported provider, when backfill runs, then the inbound is counted as unsupported and no Task is created")]
    public async Task UnsupportedProvidersCountWithoutTaskCreationAsync()
    {
        var (store, bindings) = WorkBackfillFixtureFactory.NewBackfillFixture();
        var runner = new WorkBackfillRunner(
            store,
            bindings,
            FakeProvider(new InboundItemBackfillSnapshot(
                ExternalId: "x",
                Title: "t",
                Body: "b",
                ProviderWire: "UnsupportedProvider",
                ProjectId: Project())),
            NullLogger<WorkBackfillRunner>.Instance);

        var outcome = await runner.RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        outcome.NewTasksCreated.ShouldBe(0);
        outcome.UnsupportedProviders.ShouldBe(1);
        outcome.SkippedInboundItems.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a mixed batch of inbounds, when backfill runs, then every supported inbound becomes a Task and every unsupported one is counted")]
    public async Task MixedBatchProducesExpectedCountsAsync()
    {
        var (store, bindings) = WorkBackfillFixtureFactory.NewBackfillFixture();
        var runner = new WorkBackfillRunner(
            store,
            bindings,
            FakeProvider(
                new InboundItemBackfillSnapshot("a", "A", "Aa", "GitHub", Project()),
                new InboundItemBackfillSnapshot("b", "B", "Bb", "GitLab", Project()),
                new InboundItemBackfillSnapshot("c", "C", "Cc", "Native", Project()),
                new InboundItemBackfillSnapshot("d", "D", "Dd", "YandexTracker", Project()),
                new InboundItemBackfillSnapshot("e", "E", "Ee", "Jira", Project()),
                new InboundItemBackfillSnapshot("f", "F", "Ff", "UnsupportedProvider", Project())),
            NullLogger<WorkBackfillRunner>.Instance);

        var outcome = await runner.RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        outcome.NewTasksCreated.ShouldBe(5);
        outcome.UnsupportedProviders.ShouldBe(1);
        outcome.SkippedInboundItems.ShouldBe(0);
    }

    private static Func<int, CancellationToken, Task<IReadOnlyList<InboundItemBackfillSnapshot>>> FakeProvider(
        params InboundItemBackfillSnapshot[] inboundItems)
    {
        return (limit, _) => Task.FromResult<IReadOnlyList<InboundItemBackfillSnapshot>>(inboundItems);
    }
}

