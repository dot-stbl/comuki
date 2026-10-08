using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Application.Views;
using Comuki.Modules.Integrations.Domain.Items;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Integrations.Application.Tickets;

/// <summary>
/// Manual inbox claim: launches the run for a pending ticket and stamps
/// the claim through the guarded store update — a concurrent claim (or a
/// webhook racing in) loses cleanly with a conflict instead of a second
/// run.
/// </summary>
/// <param name="store"></param>
/// <param name="runLauncher"></param>
/// <param name="logger"></param>
public sealed class ClaimInboundItemHandler(
    IIntegrationsStore store,
    IRunLauncher runLauncher,
    ILogger<ClaimInboundItemHandler> logger)
{
    /// <summary>Claims the ticket; returns the updated view carrying the run id.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <exception cref="InboundItemNotFoundException">Unknown ticket id.</exception>
    /// <exception cref="InboundItemConflictException">The ticket is not pending (already claimed, done or dismissed).</exception>
    public async Task<InboundItemView> HandleAsync(ClaimInboundItemCommand command, CancellationToken cancellationToken = default)
    {
        var ticket = await store.FindTicketAsync(command.TicketId, cancellationToken)
            ?? throw new InboundItemNotFoundException(command.TicketId);

        if (ticket.Status is not InboundItemStatus.Pending)
        {
            throw new InboundItemConflictException(ticket.Id, ticket.Status.ToString());
        }

        var runId = await runLauncher.LaunchAsync(ticket.ProjectId, null, ticket, cancellationToken);

        if (!await store.TryMarkClaimedAsync(ticket.Id, runId, cancellationToken))
        {
            logger.LogWarning("Ticket {TicketId} claim lost the race — another run was launched", ticket.Id);
            throw new InboundItemConflictException(ticket.Id, "claimed concurrently");
        }

        logger.LogInformation("Ticket {TicketId} claimed into run {RunId} (inbox)", ticket.Id, runId);

        return InboundItemView.Of(ticket) with { Status = nameof(InboundItemStatus.Claimed), RunId = runId.Value };
    }
}
