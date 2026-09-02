using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// The Jira source provider: webhook acceptance (secret query param,
/// body-hash delivery id — Jira sends no delivery guid) and the JQL
/// catalog.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
/// <param name="clock"></param>
public sealed class JiraTicketSourceProvider(
    TrackerClientFactory clients,
    ISecretResolver secrets,
    TimeProvider clock) : ITicketSourceProvider
{
    private const int PageSize = 25;

    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.Jira;

    /// <inheritdoc />
    public string DeliveryIdOf(WebhookDelivery delivery)
    {
        // Jira webhooks carry no delivery guid — the raw body hash is
        // the stable letter id
        return ProviderDeliveryIds.BodyHash(delivery.Body);
    }

    /// <inheritdoc />
    public bool VerifySignature(SourceConnection connection, WebhookDelivery delivery)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        return JiraWebhookVerifier.Verify(
            secrets.Resolve(connection.SecretEnvRef),
            delivery.QueryParam(settings.WebhookSecretParam));
    }

    /// <inheritdoc />
    public IncomingTicket? Normalize(WebhookDelivery delivery, SourceConnection connection)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        return JiraPayloadMapper.ToTicket(delivery.Body, settings.Site, connection.ProjectId, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingTicket>> FetchCatalogAsync(SourceConnection connection, int page, CancellationToken cancellationToken = default)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        var api = clients.Jira(settings.Site, secrets.Resolve(settings.ApiTokenEnv));
        var result = await api.SearchAsync(settings.Jql, PageSize, (page - 1) * PageSize, cancellationToken);

        return [.. result.Issues.Select(issue => JiraPayloadMapper.ToTicket(issue, settings.Site, connection.ProjectId, clock.GetUtcNow()))];
    }
}
