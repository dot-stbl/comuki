using Comuki.Modules.Integrations.Domain.Ids;

namespace Comuki.Modules.Integrations.Application.Tickets;

/// <summary>Claims one inbox ticket into a run (permission <c>integration:claim</c>).</summary>
/// <param name="TicketId"></param>
public sealed record ClaimInboundItemCommand(InboundItemId TicketId);
