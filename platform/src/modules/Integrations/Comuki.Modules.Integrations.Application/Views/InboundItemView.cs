using Comuki.Modules.Integrations.Domain.Items;

namespace Comuki.Modules.Integrations.Application.Views;

/// <summary>
/// Read-model of an integrations ticket for the inbox and the API surfaces.
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
public sealed record InboundItemView(
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
    public static InboundItemView Of(InboundItem ticket)
    {
        return new InboundItemView(
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
