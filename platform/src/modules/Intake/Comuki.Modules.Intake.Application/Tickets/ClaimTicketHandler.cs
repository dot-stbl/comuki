using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Tickets;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>
/// Manual inbox claim: launches the run for a pending ticket and stamps
/// the claim through the guarded store update — a concurrent claim (or a
/// webhook racing in) loses cleanly with a conflict instead of a second
/// run.
/// </summary>
/// <param name="store"></param>
/// <param name="runLauncher"></param>
/// <param name="logger"></param>
public sealed class ClaimTicketHandler(
    IIntakeStore store,
    IRunLauncher runLauncher,
    ILogger<ClaimTicketHandler> logger)
{
    /// <summary>Claims the ticket; returns the updated view carrying the run id.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="IntakeTicketNotFoundException">Unknown ticket id.</exception>
    /// <exception cref="IntakeTicketConflictException">The ticket is not pending (already claimed, done or dismissed).</exception>
    public async Task<IntakeTicketView> HandleAsync(ClaimTicketCommand command, CancellationToken cancellationToken = default)
    {
        var ticket = await store.FindTicketAsync(command.TicketId, cancellationToken)
            ?? throw new IntakeTicketNotFoundException(command.TicketId);

        if (ticket.Status is not IntakeTicketStatus.Pending)
        {
            throw new IntakeTicketConflictException(ticket.Id, ticket.Status.ToString());
        }

        var runId = await runLauncher.LaunchAsync(ticket.ProjectId, null, ticket, cancellationToken);

        if (!await store.TryMarkClaimedAsync(ticket.Id, runId, cancellationToken))
        {
            logger.LogWarning("Ticket {TicketId} claim lost the race — another run was launched", ticket.Id);
            throw new IntakeTicketConflictException(ticket.Id, "claimed concurrently");
        }

        logger.LogInformation("Ticket {TicketId} claimed into run {RunId} (inbox)", ticket.Id, runId);

        return IntakeTicketView.Of(ticket) with { Status = "Claimed", RunId = runId.Value };
    }
}
