using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Unit.Fakes;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Tests for the in-memory <see cref="IWorkInboxWatermarkStore"/>
/// double. The EF-backed implementation (the
/// <c>work.outbox_watermarks</c> table) uses a
/// <c>GREATEST(...)</c> upsert so the monotonic-direction contract
/// is the SQL engine's; these tests assert the in-memory double
/// carries the same contract (per-type <c>Guid</c> progress,
/// <see cref="Guid.Empty"/> for unseen types, never regresses on a
/// smaller id) so the per-cycle subscriber logic that depends on
/// it sees a faithful mirror of production behavior.
/// </summary>
public sealed class WorkInboxWatermarkStoreShould
{
    [Fact(DisplayName = "Given a fresh store, when GetAsync is called for an unseen type, then it returns Guid.Empty")]
    public async Task GetReturnsEmptyForUnseenTypeAsync()
    {
        IWorkInboxWatermarkStore store = new InMemoryWorkInboxWatermarkStore();

        var watermark = await store.GetAsync("work.task.dispatch.requested.v1", TestContext.Current.CancellationToken);

        watermark.ShouldBe(Guid.Empty);
    }

    [Fact(DisplayName = "Given a store, when RecordAsync advances a type, then GetAsync returns the new last-seen id")]
    public async Task RecordAdvancesWatermarkAsync()
    {
        IWorkInboxWatermarkStore store = new InMemoryWorkInboxWatermarkStore();
        var firstId = new Guid("00000000-1111-2222-3333-444444444444");
        var secondId = new Guid("11111111-2222-3333-4444-555555555555");

        await store.RecordAsync("work.task.dispatch.requested.v1", firstId, TestContext.Current.CancellationToken);
        var afterFirst = await store.GetAsync("work.task.dispatch.requested.v1", TestContext.Current.CancellationToken);

        await store.RecordAsync("work.task.dispatch.requested.v1", secondId, TestContext.Current.CancellationToken);
        var afterSecond = await store.GetAsync("work.task.dispatch.requested.v1", TestContext.Current.CancellationToken);

        afterFirst.ShouldBe(firstId);
        afterSecond.ShouldBe(secondId);
    }

    [Fact(DisplayName = "Given a store with an advanced watermark, when RecordAsync is called with a smaller id, then the watermark does not regress")]
    public async Task RecordDoesNotRegressOnSmallerIdAsync()
    {
        IWorkInboxWatermarkStore store = new InMemoryWorkInboxWatermarkStore();
        var higher = new Guid("11111111-2222-3333-4444-555555555555");
        var lower = new Guid("00000000-1111-2222-3333-444444444444");

        await store.RecordAsync("work.task.attempt.cancelled.v1", higher, TestContext.Current.CancellationToken);
        await store.RecordAsync("work.task.attempt.cancelled.v1", lower, TestContext.Current.CancellationToken);

        var current = await store.GetAsync("work.task.attempt.cancelled.v1", TestContext.Current.CancellationToken);

        current.ShouldBe(higher);
    }

    [Fact(DisplayName = "Given a store, when RecordAsync is called for two different types, then each type's watermark is independent")]
    public async Task PerTypeWatermarksAreIndependentAsync()
    {
        IWorkInboxWatermarkStore store = new InMemoryWorkInboxWatermarkStore();
        var dispatchId = new Guid("00000000-1111-2222-3333-444444444444");
        var cancelId = new Guid("11111111-2222-3333-4444-555555555555");

        await store.RecordAsync("work.task.dispatch.requested.v1", dispatchId, TestContext.Current.CancellationToken);
        await store.RecordAsync("work.task.attempt.cancelled.v1", cancelId, TestContext.Current.CancellationToken);

        var dispatch = await store.GetAsync("work.task.dispatch.requested.v1", TestContext.Current.CancellationToken);
        var cancel = await store.GetAsync("work.task.attempt.cancelled.v1", TestContext.Current.CancellationToken);

        dispatch.ShouldBe(dispatchId);
        cancel.ShouldBe(cancelId);
    }
}
