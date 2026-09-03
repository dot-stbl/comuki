using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// The GitLab source provider: webhook acceptance (delivery id from
/// <c>X-Gitlab-Event-UUID</c>, token compare, tolerant normalization for
/// both issue and merge-request events) and the inbox catalog (issues
/// by default, MRs when the connection opts in via the
/// <c>includeMergeRequests</c> settings flag — never mixed in without
/// that signal).
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
        var now = clock.GetUtcNow();
        var issues = await api.ListIssuesAsync(settings.ProjectId, "opened", PageSize, page, cancellationToken);

        var result = new List<IncomingTicket>(
            issues.Select(issue => GitLabPayloadMapper.ToTicket(issue, settings.ProjectPath, connection.ProjectId, now)));

        if (settings.IncludeMergeRequests)
        {
            var mergeRequests = await api.ListMergeRequestsAsync(settings.ProjectId, "opened", PageSize, page, cancellationToken);
            result.AddRange(mergeRequests.Select(mr => GitLabPayloadMapper.ToTicket(mr, settings.ProjectPath, connection.ProjectId, now)));
        }

        return result;
    }
}
