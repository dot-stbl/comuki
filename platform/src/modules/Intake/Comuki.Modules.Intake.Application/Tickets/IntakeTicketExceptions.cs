using Comuki.Modules.Intake.Domain.Ids;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>Thrown when a ticket is not in the claimable Pending state (409).</summary>
/// <param name="TicketId"></param>
/// <param name="Status"></param>
public sealed class IntakeTicketConflictException(IncomingTicketId TicketId, string Status)
    : Exception($"intake ticket '{TicketId}' is not claimable (status {Status})");

/// <summary>Thrown when a ticket id is unknown (404).</summary>
/// <param name="TicketId"></param>
public sealed class IntakeTicketNotFoundException(IncomingTicketId TicketId)
    : Exception($"intake ticket '{TicketId}' not found");
