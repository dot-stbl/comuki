using Comuki.Host.Realtime.Models;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Truth table of the attention mapping: which journal transitions reach
/// the <c>project:{id}:attention</c> group and with which kind.
/// </summary>
public sealed class AttentionMapShould
{
    private static readonly DateTimeOffset occurredAt = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static RunEventEntry Entry(string type, string payloadJson)
    {
        return new RunEventEntry(
            Guid.NewGuid(),
            new RunId(Guid.NewGuid()),
            type,
            payloadJson,
            occurredAt);
    }

    [Theory(DisplayName = "Given a work-item transition, when mapped, then attention kind follows the target status")]
    [InlineData("work_item.status_changed", "Queued", "Running", AttentionMap.KindRunning)]
    [InlineData("work_item.status_changed", "Running", "Failed", AttentionMap.KindFailed)]
    [InlineData("work_item.lease_expired", "Running", "Failed", AttentionMap.KindFailed)]
    public void MapWorkItemAttention(string type, string from, string to, string expectedKind)
    {
        var itemId = Guid.NewGuid();
        var entry = Entry(type, $$"""{"itemId":"{{itemId}}","from":"{{from}}","to":"{{to}}","attempt":2}""");

        var draft = AttentionMap.FromEntry(entry);

        draft.ShouldNotBeNull();
        draft.AttentionKind.ShouldBe(expectedKind);
        draft.Status.ShouldBe(to);
        draft.WorkItemId.ShouldBe(itemId);
    }

    [Theory(DisplayName = "Given a non-attention-worthy transition, when mapped, then no attention is emitted")]
    [InlineData("work_item.status_changed", "Running", "Succeeded")]
    [InlineData("work_item.status_changed", "Queued", "Cancelled")]
    [InlineData("work_item.status_changed", "Running", "Queued")]
    [InlineData("work_item.lease_expired", "Running", "Queued")]
    public void SkipQuietWorkItemTransitions(string type, string from, string to)
    {
        var entry = Entry(type, $$"""{"itemId":"{{Guid.NewGuid()}}","from":"{{from}}","to":"{{to}}","attempt":1}""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }

    [Theory(DisplayName = "Given a run transition, when mapped, then attention kind follows the target status")]
    [InlineData("Queued", "Running", AttentionMap.KindRunning)]
    [InlineData("Running", "Failed", AttentionMap.KindFailed)]
    [InlineData("Running", "Escalated", AttentionMap.KindEscalated)]
    [InlineData("Queued", "Waiting", AttentionMap.KindAwaitingApproval)]
    public void MapRunAttention(string from, string to, string expectedKind)
    {
        var entry = Entry(AttentionMap.RunStatusChanged, $$"""{"from":"{{from}}","to":"{{to}}"}""");

        var draft = AttentionMap.FromEntry(entry);

        draft.ShouldNotBeNull();
        draft.AttentionKind.ShouldBe(expectedKind);
        draft.Status.ShouldBe(to);
        draft.WorkItemId.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a terminal or quiet run transition, when mapped, then no attention is emitted")]
    [InlineData("Running", "Succeeded")]
    [InlineData("Waiting", "Cancelled")]
    public void SkipQuietRunTransitions(string from, string to)
    {
        var entry = Entry(AttentionMap.RunStatusChanged, $$"""{"from":"{{from}}","to":"{{to}}"}""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a worker-reported entry, when mapped, then no attention is emitted")]
    public void SkipWorkerReports()
    {
        var entry = Entry("worker.reported", /*lang=json,strict*/ """{"stage":"tool","text":"…"}""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown dotted type, when mapped, then no attention is emitted")]
    public void SkipUnknownOpenSetTypes()
    {
        var entry = Entry("worker.custom_event", /*lang=json,strict*/ """{"to":"Failed"}""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a payload without a to status, when mapped, then no attention is emitted")]
    public void SkipPayloadWithoutTargetStatus()
    {
        var entry = Entry("work_item.status_changed", /*lang=json,strict*/ """{"itemId":"018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b"}""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a malformed payload, when mapped, then no attention is emitted, never a throw")]
    public void TolerateMalformedPayload()
    {
        var entry = Entry("work_item.status_changed", """{not json""");

        AttentionMap.FromEntry(entry).ShouldBeNull();
    }
}
