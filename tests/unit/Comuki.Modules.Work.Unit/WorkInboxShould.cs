using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Unit.Fakes;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// Tests for the in-memory <see cref="IWorkInbox"/> double used by
/// the Work T0 handler suites. The interface is the application-side
/// dedupe ledger (the EF implementation lives in
/// <c>Comuki.Modules.Work.Infrastructure.Persistence.Stores.EfWorkInbox</c>);
/// these tests assert the in-memory double's claim-once semantics so
/// the handler suites that depend on it stand on a verified
/// primitive, not on an incidental side-effect of a
/// <c>HashSet&lt;string&gt;</c>.
/// </summary>
public sealed class WorkInboxShould
{
    [Fact(DisplayName = "Given a fresh inbox, when TryClaimAsync is called with a new id, then it returns true")]
    public async Task TryClaimNewIdReturnsTrueAsync()
    {
        IWorkInbox inbox = new InMemoryWorkInbox();

        var claimed = await inbox.TryClaimAsync("work.task.11111111-1111-7111-8111-111111111111:dispatch:1", TestContext.Current.CancellationToken);

        claimed.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an inbox that has already claimed an id, when TryClaimAsync is called with the same id, then it returns false")]
    public async Task TryClaimSameIdReturnsFalseAsync()
    {
        IWorkInbox inbox = new InMemoryWorkInbox();
        const string MessageId = "work.task.22222222-2222-7222-8222-222222222222:dispatch:1";

        var first = await inbox.TryClaimAsync(MessageId, TestContext.Current.CancellationToken);
        var second = await inbox.TryClaimAsync(MessageId, TestContext.Current.CancellationToken);
        var third = await inbox.TryClaimAsync(MessageId, TestContext.Current.CancellationToken);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        third.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an inbox, when TryClaimAsync is called with distinct ids, then each call returns true")]
    public async Task TryClaimDistinctIdsAllReturnTrueAsync()
    {
        IWorkInbox inbox = new InMemoryWorkInbox();

        var first = await inbox.TryClaimAsync("work.task.33333333-3333-7333-8333-333333333333:dispatch:1", TestContext.Current.CancellationToken);
        var second = await inbox.TryClaimAsync("work.task.33333333-3333-7333-8333-333333333333:dispatch:2", TestContext.Current.CancellationToken);
        var third = await inbox.TryClaimAsync("work.task.33333333-3333-7333-8333-333333333333:terminal:1", TestContext.Current.CancellationToken);

        first.ShouldBeTrue();
        second.ShouldBeTrue();
        third.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an inbox, when TryClaimAsync is called with ids that differ only in case, then they are treated as distinct (ordinal comparison)")]
    public async Task TryClaimIsCaseSensitiveAsync()
    {
        IWorkInbox inbox = new InMemoryWorkInbox();

        var lower = await inbox.TryClaimAsync("work.task.44444444-4444-7444-8444-444444444444:dispatch:1", TestContext.Current.CancellationToken);
        var upper = await inbox.TryClaimAsync("WORK.TASK.44444444-4444-7444-8444-444444444444:DISPATCH:1", TestContext.Current.CancellationToken);

        lower.ShouldBeTrue();
        upper.ShouldBeTrue();
    }
}
