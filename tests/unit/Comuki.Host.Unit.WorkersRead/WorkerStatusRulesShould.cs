using Comuki.Host.Workers.Read;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkersRead;

/// <summary>Pure derivation rules of the derived worker registry.</summary>
public sealed class WorkerStatusRulesShould
{
    private static readonly DateTimeOffset fixedNow = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a worker without a live lease, when derived, then it is idle regardless of staleness")]
    public void DeriveIdleWithoutLease()
    {
        WorkerStatusRules.DeriveState(isBusy: false, lastSeenAt: fixedNow.AddHours(-2), staleAfter: TimeSpan.FromMinutes(3), fixedNow)
            .ShouldBe("idle");
    }

    [Fact(DisplayName = "Given a busy worker with a fresh heartbeat, when derived, then it is busy")]
    public void DeriveBusyWithFreshHeartbeat()
    {
        WorkerStatusRules.DeriveState(isBusy: true, lastSeenAt: fixedNow.AddSeconds(-30), staleAfter: TimeSpan.FromMinutes(3), fixedNow)
            .ShouldBe("busy");
    }

    [Fact(DisplayName = "Given a busy worker with a stale heartbeat past lease TTL + grace, when derived, then it is offline")]
    public void DeriveOfflineWithStaleHeartbeat()
    {
        WorkerStatusRules.DeriveState(isBusy: true, lastSeenAt: fixedNow.AddMinutes(-5), staleAfter: TimeSpan.FromMinutes(3), fixedNow)
            .ShouldBe("offline");
    }

    [Fact(DisplayName = "Given a heartbeat exactly on the stale boundary, when derived, then it stays busy (boundary is inclusive)")]
    public void DeriveBusyOnExactBoundary()
    {
        WorkerStatusRules.DeriveState(isBusy: true, lastSeenAt: fixedNow.AddMinutes(-3), staleAfter: TimeSpan.FromMinutes(3), fixedNow)
            .ShouldBe("busy");
    }
}

/// <summary>Journal-claim parsing of <c>work_item.status_changed</c> payloads.</summary>
public sealed class WorkerStatusRulesParseClaimWorkerShould
{
    private static readonly Guid claimWorkerId = Guid.Parse("018f5b2e-6f1c-7c3a-9d4e-2a5b6c7d8e9f");

    [Fact(DisplayName = "Given a claim payload (to == Running with workerId), when parsed, then the worker id returns")]
    public void ParseClaimEvent()
    {
        var payload = $$"""{"itemId":"{{Guid.NewGuid()}}","from":"Queued","to":"Running","workerId":"{{claimWorkerId}}","attempt":2}""";

        WorkerStatusRules.ParseClaimWorker(payload).ShouldBe(new WorkerId(claimWorkerId));
    }

    [Fact(DisplayName = "Given a complete/fail payload (to != Running, no workerId), when parsed, then it is skipped")]
    public void SkipTerminalEvent()
    {
        var payload = $$$"""{"itemId":"{{{Guid.NewGuid()}}}","from":"Running","to":"Succeeded","detail":{"result":"ok"}}""";

        WorkerStatusRules.ParseClaimWorker(payload).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a reaper payload (lease_expired shape), when parsed, then it is skipped")]
    public void SkipReaperEvent()
    {
        var payload = $$"""{"itemId":"{{Guid.NewGuid()}}","from":"Running","to":"Queued","attempt":3}""";

        WorkerStatusRules.ParseClaimWorker(payload).ShouldBeNull();
    }

    [Fact(DisplayName = "Given malformed JSON, when parsed, then it is skipped without throwing")]
    public void SkipMalformedJson()
    {
        WorkerStatusRules.ParseClaimWorker("not json").ShouldBeNull();
    }
}
