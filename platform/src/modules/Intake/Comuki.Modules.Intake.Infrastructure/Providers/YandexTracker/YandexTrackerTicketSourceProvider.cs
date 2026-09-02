using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// The Yandex Tracker source provider: webhook acceptance (shared-secret
/// header, body-hash delivery id — Tracker sends no delivery guid) and
/// the poll-based catalog over the issues search API.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
/// <param name="clock"></param>
public sealed class YandexTrackerTicketSourceProvider(
    TrackerClientFactory clients,
    ISecretResolver secrets,
    TimeProvider clock) : ITicketSourceProvider
{
    private const int PageSize = 25;

    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.YandexTracker;

    /// <inheritdoc />
    public string DeliveryIdOf(WebhookDelivery delivery)
    {
        // Tracker webhooks carry no delivery guid — the raw body hash is
        // the stable letter id
        return ProviderDeliveryIds.BodyHash(delivery.Body);
    }

    /// <inheritdoc />
    public bool VerifySignature(SourceConnection connection, WebhookDelivery delivery)
    {
        var settings = YandexTrackerSettings.Parse(connection.SettingsJson);
        return YandexTrackerWebhookVerifier.Verify(
            secrets.Resolve(connection.SecretEnvRef),
            delivery.Header(settings.WebhookSecretHeader));
    }

    /// <inheritdoc />
    public IncomingTicket? Normalize(WebhookDelivery delivery, SourceConnection connection)
    {
        return YandexTrackerPayloadMapper.ToTicket(delivery.Body, connection.ProjectId, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingTicket>> FetchCatalogAsync(SourceConnection connection, int page, CancellationToken cancellationToken = default)
    {
        var settings = YandexTrackerSettings.Parse(connection.SettingsJson);
        var api = clients.YandexTracker(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv), settings.OrgId);
        var issues = await api.SearchIssuesAsync(
            new TrackerSearchBody($"Queue: \"{settings.Queue}\" Status: \"Open\""),
            cancellationToken);

        // the API pages via the find/limit fields; the provider keeps the
        // single most recent page — enough for the inbox browse view
        return [.. issues
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(issue => YandexTrackerPayloadMapper.ToTicket(issue, connection.ProjectId, clock.GetUtcNow()))];
    }
}
