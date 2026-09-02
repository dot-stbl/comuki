using Comuki.Host.Realtime.Models;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Journal entry → <see cref="RunEventView"/> mapping: the slim wire shape
/// (unix-ms timestamps, work-item id extraction, payload size cap).
/// </summary>
public sealed class RunEventViewMappingShould
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

    [Fact(DisplayName = "Given a work-item entry, when mapped, then the view carries run id, type, item id and unix-ms timestamp")]
    public void MapWorkItemEntry()
    {
        var itemId = Guid.NewGuid();
        var entry = Entry(
            "work_item.status_changed",
            $$"""{"itemId":"{{itemId}}","from":"Queued","to":"Running","attempt":1}""");

        var view = RunEventViewMapping.ToView(entry);

        view.RunId.ShouldBe(entry.RunId.Value);
        view.Type.ShouldBe("work_item.status_changed");
        view.WorkItemId.ShouldBe(itemId);
        view.OccurredAtUnixMs.ShouldBe(occurredAt.ToUnixTimeMilliseconds());
        view.PayloadJson.ShouldBe(entry.PayloadJson);
        view.PayloadOmitted.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a run entry without itemId, when mapped, then the work item id is null")]
    public void MapRunEntryWithoutItemId()
    {
        var entry = Entry("run.status_changed", /*lang=json,strict*/ """{"from":"Queued","to":"Running"}""");

        var view = RunEventViewMapping.ToView(entry);

        view.WorkItemId.ShouldBeNull();
        view.PayloadOmitted.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an oversized payload, when mapped, then the payload is omitted and flagged")]
    public void OmitOversizedPayload()
    {
        var payloadJson = """{"itemId":""" + $@"""{Guid.NewGuid()}"",""detail"":""" + new string('x', RunEventViewMapping.MaxPayloadJsonChars) + "\"}";
        var entry = Entry("work_item.status_changed", payloadJson);

        var view = RunEventViewMapping.ToView(entry);

        view.PayloadJson.ShouldBeNull();
        view.PayloadOmitted.ShouldBeTrue();
        view.WorkItemId.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a payload at exactly the cap, when mapped, then the payload rides along")]
    public void KeepPayloadAtCapBoundary()
    {
        // {"detail":"…"} — the wrapper is 13 characters, so the filler
        // lands the total at exactly MaxPayloadJsonChars; the cap is > not >=
        var filler = new string('x', RunEventViewMapping.MaxPayloadJsonChars - 13);
        var entry = Entry("work_item.status_changed", $$"""{"detail":"{{filler}}"}""");

        var view = RunEventViewMapping.ToView(entry);

        entry.PayloadJson.Length.ShouldBe(RunEventViewMapping.MaxPayloadJsonChars);
        view.PayloadJson.ShouldBe(entry.PayloadJson);
        view.PayloadOmitted.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a malformed payload, when the item id is read, then the answer is null, never a throw")]
    public void TolerateMalformedPayload()
    {
        var entry = Entry("work_item.status_changed", """not json""");

        RunEventViewMapping.ReadWorkItemId(entry).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a payload with a non-guid itemId, when the item id is read, then the answer is null")]
    public void TolerateNonGuidItemId()
    {
        var entry = Entry("work_item.status_changed", /*lang=json,strict*/ """{"itemId":"not-a-guid"}""");

        RunEventViewMapping.ReadWorkItemId(entry).ShouldBeNull();
    }
}
