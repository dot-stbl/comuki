using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Integrations.Application.Tickets;

/// <summary>Thrown when an inbound item is not in the claimable Pending state (409).</summary>
/// <param name="TicketId"></param>
/// <param name="Status"></param>
public sealed class InboundItemConflictException(InboundItemId TicketId, string Status)
    : DomainException(ErrorCode, $"inbound item '{TicketId}' is not claimable (status {Status})")
{
    private const string ErrorCode = "integration.inbound_item_conflict";
}

/// <summary>Thrown when an inbound item id is unknown (404).</summary>
public sealed class InboundItemNotFoundException(InboundItemId TicketId)
    : DomainException(ErrorCode, $"inbound item '{TicketId}' not found")
{
    private const string ErrorCode = "integration.inbound_item_not_found";
}
