using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// The GitHub source provider: webhook acceptance (delivery id from
/// <c>X-GitHub-Delivery</c>, HMAC-SHA256 verification, tolerant payload
/// normalization) and the open-issues catalog (pull requests filtered
/// out).
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
/// <param name="clock"></param>
public sealed class GitHubTicketSourceProvider(
    TrackerClientFactory clients,
    ISecretResolver secrets,
    TimeProvider clock) : ITicketSourceProvider
{
    private const int PageSize = 25;

    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.GitHub;

    /// <inheritdoc />
    public string DeliveryIdOf(WebhookDelivery delivery)
    {
        return delivery.Header("X-GitHub-Delivery") is { Length: > 0 } deliveryId
            ? deliveryId
            : ProviderDeliveryIds.BodyHash(delivery.Body);
    }

    /// <inheritdoc />
    public bool VerifySignature(SourceConnection connection, WebhookDelivery delivery)
    {
        return GitHubWebhookVerifier.Verify(secrets.Resolve(connection.SecretEnvRef), delivery.Header("X-Hub-Signature-256"), delivery.Body.Span);
    }

    /// <inheritdoc />
    public IncomingTicket? Normalize(WebhookDelivery delivery, SourceConnection connection)
    {
        return GitHubPayloadMapper.ToTicket(delivery.Body, connection.ProjectId, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingTicket>> FetchCatalogAsync(SourceConnection connection, int page, CancellationToken cancellationToken = default)
    {
        var settings = GitHubSettings.Parse(connection.SettingsJson);
        var api = clients.GitHub(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv));
        var issues = await api.ListIssuesAsync(settings.Owner, settings.Repo, "open", PageSize, page, cancellationToken);

        return [.. issues
            .Where(static issue => issue.IsIssue)
            .Select(issue => GitHubPayloadMapper.ToTicket(issue, settings.Owner, settings.Repo, connection.ProjectId, clock.GetUtcNow()))];
    }
}
