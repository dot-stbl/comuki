using Comuki.Modules.Integrations.Application.Ports.Sources;
using Comuki.Modules.Integrations.Application.Ports.Sync;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Integrations.Infrastructure.Providers.Jira;

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
    public async Task<bool> VerifySignatureAsync(SourceConnection connection, WebhookDelivery delivery, CancellationToken cancellationToken = default)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        var secret = await secrets.ResolveAsync(connection.SecretEnvRef, cancellationToken);
        return JiraWebhookVerifier.Verify(
            secret,
            delivery.QueryParam(settings.WebhookSecretParam));
    }

    /// <inheritdoc />
    public InboundItem? Normalize(WebhookDelivery delivery, SourceConnection connection)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        return JiraPayloadMapper.ToTicket(delivery.Body, settings.Site, connection.ProjectId, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboundItem>> FetchCatalogAsync(SourceConnection connection, int page, CancellationToken cancellationToken = default)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        var api = clients.Jira(
            settings.Site,
            await secrets.ResolveAsync(settings.ApiTokenEnv, cancellationToken));
        var result = await api.SearchAsync(settings.Jql, PageSize, (page - 1) * PageSize, cancellationToken);

        return [.. result.Issues.Select(issue => JiraPayloadMapper.ToTicket(issue, settings.Site, connection.ProjectId, clock.GetUtcNow()))];
    }
}
