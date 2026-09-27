using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>Thrown when a ticket is not in the claimable Pending state (409).</summary>
/// <param name="TicketId"></param>
/// <param name="Status"></param>
public sealed class IntakeTicketConflictException(IncomingTicketId TicketId, string Status)
    : DomainException(ErrorCode, $"intake ticket '{TicketId}' is not claimable (status {Status})")
{
    private const string ErrorCode = "intake.ticket_conflict";
}

/// <summary>Thrown when a ticket id is unknown (404).</summary>
/// <param name="TicketId"></param>
public sealed class IntakeTicketNotFoundException(IncomingTicketId TicketId)
    : DomainException(ErrorCode, $"intake ticket '{TicketId}' not found")
{
    private const string ErrorCode = "intake.ticket_not_found";
}
