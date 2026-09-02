using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Inbox claim handler — happy path, missing ticket, not-pending, race loss.</summary>
public sealed class ClaimTicketHandlerShould
{
    private readonly DateTimeOffset now = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
    private readonly IIntakeStore store = Substitute.For<IIntakeStore>();
    private readonly IRunLauncher runLauncher = Substitute.For<IRunLauncher>();

    [Fact(DisplayName = "Given a pending ticket, when Claim runs, then a run is launched and the view is Claimed")]
    public async Task ClaimPendingAsync()
    {
        var ticket = IncomingTicket.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "acme/app#1",
            "Title",
            "Body",
            "ada",
            "https://example.com/1",
            "acme/app",
            [],
            now);
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var runId = RunId.New();
        runLauncher.LaunchAsync(ticket.ProjectId, ticket, Arg.Any<CancellationToken>()).Returns(runId);
        store.TryMarkClaimedAsync(ticket.Id, runId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new ClaimTicketHandler(store, runLauncher, NullLogger<ClaimTicketHandler>.Instance);

        var view = await handler.HandleAsync(new ClaimTicketCommand(ticket.Id), TestContext.Current.CancellationToken);

        view.Status.ShouldBe("Claimed");
        view.RunId.ShouldBe(runId.Value);
    }

    [Fact(DisplayName = "Given a missing ticket, when Claim runs, then IntakeTicketNotFoundException is thrown")]
    public async Task MissingTicketThrowsAsync()
    {
        store.FindTicketAsync(Arg.Any<IncomingTicketId>(), Arg.Any<CancellationToken>()).Returns((IncomingTicket?)null);
        var handler = new ClaimTicketHandler(store, runLauncher, NullLogger<ClaimTicketHandler>.Instance);

        await Should.ThrowAsync<IntakeTicketNotFoundException>(
            () => handler.HandleAsync(new ClaimTicketCommand(IncomingTicketId.New()), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a non-pending ticket, when Claim runs, then IntakeTicketConflictException is thrown")]
    public async Task NonPendingThrowsAsync()
    {
        var ticket = IncomingTicket.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            now);
        ticket.MarkDismissed(now);
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var handler = new ClaimTicketHandler(store, runLauncher, NullLogger<ClaimTicketHandler>.Instance);

        await Should.ThrowAsync<IntakeTicketConflictException>(
            () => handler.HandleAsync(new ClaimTicketCommand(ticket.Id), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a lost claim race, when Claim runs, then IntakeTicketConflictException is thrown")]
    public async Task LostRaceThrowsAsync()
    {
        var ticket = IncomingTicket.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            now);
        store.FindTicketAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        runLauncher.LaunchAsync(ticket.ProjectId, ticket, Arg.Any<CancellationToken>()).Returns(RunId.New());
        store.TryMarkClaimedAsync(Arg.Any<IncomingTicketId>(), Arg.Any<RunId>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ClaimTicketHandler(store, runLauncher, NullLogger<ClaimTicketHandler>.Instance);

        await Should.ThrowAsync<IntakeTicketConflictException>(
            () => handler.HandleAsync(new ClaimTicketCommand(ticket.Id), TestContext.Current.CancellationToken));
    }
}
