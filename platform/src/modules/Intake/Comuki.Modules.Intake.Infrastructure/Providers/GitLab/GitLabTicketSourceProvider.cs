using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// The GitLab source provider: webhook acceptance (delivery id from
/// <c>X-Gitlab-Event-UUID</c>, token compare, tolerant normalization)
/// and the opened-issues catalog.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
/// <param name="clock"></param>
public sealed class GitLabTicketSourceProvider(
    TrackerClientFactory clients,
    ISecretResolver secrets,
    TimeProvider clock) : ITicketSourceProvider
{
    private const int PageSize = 25;

    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.GitLab;

    /// <inheritdoc />
    public string DeliveryIdOf(WebhookDelivery delivery)
    {
        return delivery.Header("X-Gitlab-Event-UUID") is { Length: > 0 } deliveryId
            ? deliveryId
            : ProviderDeliveryIds.BodyHash(delivery.Body);
    }

    /// <inheritdoc />
    public bool VerifySignature(SourceConnection connection, WebhookDelivery delivery)
    {
        return GitLabWebhookVerifier.Verify(secrets.Resolve(connection.SecretEnvRef), delivery.Header("X-Gitlab-Token"));
    }

    /// <inheritdoc />
    public IncomingTicket? Normalize(WebhookDelivery delivery, SourceConnection connection)
    {
        return GitLabPayloadMapper.ToTicket(delivery.Body, connection.ProjectId, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingTicket>> FetchCatalogAsync(SourceConnection connection, int page, CancellationToken cancellationToken = default)
    {
        var settings = GitLabSettings.Parse(connection.SettingsJson);
        var api = clients.GitLab(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv));
        var issues = await api.ListIssuesAsync(settings.ProjectId, "opened", PageSize, page, cancellationToken);

        return [.. issues.Select(issue => GitLabPayloadMapper.ToTicket(issue, settings.ProjectPath, connection.ProjectId, clock.GetUtcNow()))];
    }
}
