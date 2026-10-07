using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Application.Tickets;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Integrations.Unit;

/// <summary>Inbox claim handler — happy path, missing ticket, not-pending, race loss.</summary>
public sealed class ClaimInboundItemHandlerShould
{
    private readonly DateTimeOffset now = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
    private readonly IIntegrationsStore store = Substitute.For<IIntegrationsStore>();
    private readonly IRunLauncher runLauncher = Substitute.For<IRunLauncher>();

    [Fact(DisplayName = "Given a pending ticket, when Claim runs, then a run is launched and the view is Claimed")]
    public async Task ClaimPendingAsync()
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
            [],
            InboundItemKind.Issue,
            now);
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var runId = RunId.New();
        runLauncher.LaunchAsync(ticket.ProjectId, Arg.Any<Domain.Connections.SourceConnection>(), ticket, Arg.Any<CancellationToken>()).Returns(runId);
        store.TryMarkClaimedAsync(ticket.Id, runId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ClaimInboundItemHandler(store, runLauncher, NullLogger<ClaimInboundItemHandler>.Instance);

        var view = await handler.HandleAsync(new ClaimInboundItemCommand(ticket.Id), TestContext.Current.CancellationToken);

        view.Status.ShouldBe("Claimed");
        view.RunId.ShouldBe(runId.Value);
    }

    [Fact(DisplayName = "Given a missing ticket, when Claim runs, then InboundItemNotFoundException is thrown")]
    public async Task MissingTicketThrowsAsync()
    {
        store.FindTicketAsync(Arg.Any<InboundItemId>(), Arg.Any<CancellationToken>()).Returns((InboundItem?)null);
        var handler = new ClaimInboundItemHandler(store, runLauncher, NullLogger<ClaimInboundItemHandler>.Instance);

        await Should.ThrowAsync<InboundItemNotFoundException>(
            () => handler.HandleAsync(new ClaimInboundItemCommand(InboundItemId.New()), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a non-pending ticket, when Claim runs, then InboundItemConflictException is thrown")]
    public async Task NonPendingThrowsAsync()
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
        ticket.MarkDismissed(now);
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var handler = new ClaimInboundItemHandler(store, runLauncher, NullLogger<ClaimInboundItemHandler>.Instance);

        await Should.ThrowAsync<InboundItemConflictException>(
            () => handler.HandleAsync(new ClaimInboundItemCommand(ticket.Id), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a lost claim race, when Claim runs, then InboundItemConflictException is thrown")]
    public async Task LostRaceThrowsAsync()
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
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        runLauncher.LaunchAsync(ticket.ProjectId, Arg.Any<Domain.Connections.SourceConnection>(), ticket, Arg.Any<CancellationToken>()).Returns(RunId.New());
        store.TryMarkClaimedAsync(Arg.Any<InboundItemId>(), Arg.Any<RunId>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ClaimInboundItemHandler(store, runLauncher, NullLogger<ClaimInboundItemHandler>.Instance);

        await Should.ThrowAsync<InboundItemConflictException>(
            () => handler.HandleAsync(new ClaimInboundItemCommand(ticket.Id), TestContext.Current.CancellationToken));
    }
}
