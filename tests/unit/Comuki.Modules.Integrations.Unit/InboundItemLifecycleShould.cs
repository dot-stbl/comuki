using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Integrations.Unit;

/// <summary>InboundItem status transitions: claim / done / dismiss / bind.</summary>
public sealed class InboundItemLifecycleShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 22, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a pending ticket, when MarkClaimed then MarkDone, then statuses advance")]
    public void ClaimThenDone()
    {
        var ticket = InboundItem.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "acme/app#1",
            "Title",
            "Body",
            "ada",
            "https://example.com/1",
            "acme/app",
            ["bug"],
            InboundItemKind.Issue,
            now);
        var runId = RunId.New();
        var claimedAt = now.AddMinutes(1);
        var doneAt = now.AddMinutes(2);

        ticket.BindConnection(SourceConnectionId.New());
        ticket.MarkClaimed(runId, claimedAt);
        ticket.MarkDone(doneAt);

        ticket.Status.ShouldBe(InboundItemStatus.Done);
        ticket.RunId.ShouldBe(runId);
        ticket.ConnectionId.ShouldNotBeNull();
        ticket.UpdatedAt.ShouldBe(doneAt);
    }

    [Fact(DisplayName = "Given a pending ticket, when MarkDismissed is called, then status is Dismissed")]
    public void DismissPending()
    {
        var ticket = InboundItem.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            InboundItemKind.Issue,
            now);

        ticket.MarkDismissed(now.AddSeconds(5));

        ticket.Status.ShouldBe(InboundItemStatus.Dismissed);
    }

    [Fact(DisplayName = "Given a claimed ticket, when MarkClaimed is called again, then InvalidOperationException is thrown")]
    public void RefuseDoubleClaim()
    {
        var ticket = InboundItem.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            InboundItemKind.Issue,
            now);
        ticket.MarkClaimed(RunId.New(), now);

        Should.Throw<InvalidOperationException>(() => ticket.MarkClaimed(RunId.New(), now.AddMinutes(1)));
    }

    [Fact(DisplayName = "Given a pending ticket, when MarkDone is called, then InvalidOperationException is thrown")]
    public void RefuseDoneFromPending()
    {
        var ticket = InboundItem.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            InboundItemKind.Issue,
            now);

        Should.Throw<InvalidOperationException>(() => ticket.MarkDone(now));
    }
}
