using Comuki.Modules.Intake.Domain.Ids;

namespace Comuki.Modules.Intake.Application.Tickets;

/// <summary>Claims one inbox ticket into a run (permission <c>intake:claim</c>).</summary>
/// <param name="TicketId"></param>
public sealed record ClaimTicketCommand(IncomingTicketId TicketId);
