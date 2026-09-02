using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Application.Views;

/// <summary>
/// Read-model of an intake ticket for the inbox and the API surfaces.
/// </summary>
/// <param name="Id"></param>
/// <param name="ProjectId"></param>
/// <param name="Source">Kebab-case provider key.</param>
/// <param name="ExternalId"></param>
/// <param name="Title"></param>
/// <param name="Url"></param>
/// <param name="Status"></param>
/// <param name="RunId">The launched run, when claimed.</param>
/// <param name="CreatedAt"></param>
public sealed record IntakeTicketView(
    Guid Id,
    Guid ProjectId,
    string Source,
    string ExternalId,
    string Title,
    string Url,
    string Status,
    Guid? RunId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="ticket"></param>
    /// <returns></returns>
    public static IntakeTicketView Of(IncomingTicket ticket)
    {
        return new IntakeTicketView(
            ticket.Id.Value,
            ticket.ProjectId.Value,
            TicketProviderKeys.Key(ticket.Provider),
            ticket.ExternalId,
            ticket.Title,
            ticket.Url,
            ticket.Status.ToString(),
            ticket.RunId?.Value,
            ticket.CreatedAt);
    }
}
