using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Application.Ports;

/// <summary>
/// The per-provider intake port: webhook acceptance (delivery id,
/// signature verification, payload normalization) plus the inbox-mode
/// catalog fetch. Implementations live in Infrastructure — one per
/// tracker, each a Refit client over the provider API plus a static
/// payload mapper.
/// </summary>
public interface ITicketSourceProvider
{
    /// <summary>Kebab-case source key — matches the webhook route segment.</summary>
    public string SourceKey { get; }

    /// <summary>
    /// The stable provider-side delivery identifier: the provider's id
    /// header when it has one, a SHA-256 of the raw body otherwise.
    /// </summary>
    /// <param name="delivery"></param>
    /// <returns></returns>
    public string DeliveryIdOf(WebhookDelivery delivery);

    /// <summary>
    /// Verifies the webhook's authenticity — the signature IS the auth on
    /// the hook surface. The verification secret resolves from
    /// <see cref="SourceConnection.SecretEnvRef"/>; a missing secret or a
    /// mismatch answers false.
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="delivery"></param>
    /// <returns></returns>
    public bool VerifySignature(SourceConnection connection, WebhookDelivery delivery);

    /// <summary>
    /// Normalizes the payload into a pending ticket; null when the event
    /// is not a ticket event for this source (ping, unrelated kinds) — a
    /// skip, not an error. Unparseable-but-ticket-shaped payloads also
    /// answer null (logged skip, 200 OK).
    /// </summary>
    /// <param name="delivery"></param>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public IncomingTicket? Normalize(WebhookDelivery delivery, ProjectId projectId);

    /// <summary>
    /// Fetches one page of the provider's issue catalog for inbox mode
    /// (list issues / JQL / tracker search).
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="page">1-based page number.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<IncomingTicket>> FetchCatalogAsync(
        SourceConnection connection,
        int page,
        CancellationToken cancellationToken = default);
}
