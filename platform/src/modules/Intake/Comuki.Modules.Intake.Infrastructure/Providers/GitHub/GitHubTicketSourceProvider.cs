using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// The GitHub source provider: webhook acceptance (delivery id from
/// <c>X-GitHub-Delivery</c>, HMAC-SHA256 verification, tolerant payload
/// normalization for both issue and pull-request events) and the
/// inbox catalog (issues by default, PRs when the connection opts in
/// via the <c>includePullRequests</c> settings flag — never mixed in
/// without that signal).
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
    public async Task<bool> VerifySignatureAsync(SourceConnection connection, WebhookDelivery delivery, CancellationToken cancellationToken = default)
    {
        var secret = await secrets.ResolveAsync(connection.SecretEnvRef, cancellationToken);
        return GitHubWebhookVerifier.Verify(secret, delivery.Header("X-Hub-Signature-256"), delivery.Body.Span);
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
        var api = clients.GitHub(
            settings.ApiBase,
            await secrets.ResolveAsync(settings.ApiTokenEnv, cancellationToken));
        var issues = await api.ListIssuesAsync(settings.Owner, settings.Repo, "open", PageSize, page, cancellationToken);

        return [.. issues
            .Where(issue => settings.IncludePullRequests || issue.IsIssue)
            .Select(issue => GitHubPayloadMapper.ToTicket(issue, settings.Owner, settings.Repo, connection.ProjectId, clock.GetUtcNow()))];
    }
}
