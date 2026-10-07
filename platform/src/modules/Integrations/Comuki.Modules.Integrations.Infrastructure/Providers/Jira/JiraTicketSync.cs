using Comuki.Modules.Integrations.Application.Ports.Sync;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Integrations.Infrastructure.Providers.Jira;

/// <summary>
/// The Jira sync-back port: a status comment with the run link on
/// every terminal transition and the configured resolved transition
/// when the run succeeded.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class JiraTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : IIntegrationSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.Jira;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, InboundItemTransition transition, CancellationToken cancellationToken = default)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        var api = clients.Jira(
            settings.Site,
            await secrets.ResolveAsync(settings.ApiTokenEnv, cancellationToken));

        await api.PostCommentAsync(transition.ExternalId, new JiraCommentBody(TrackerSyncComments.Of(transition)), cancellationToken);

        if (transition.RunStatus == RunStatuses.Succeeded && settings.ResolvedTransitionId is { Length: > 0 } transitionId)
        {
            await api.TransitionAsync(
                transition.ExternalId,
                new JiraTransitionBody(new JiraTransitionRef(transitionId)),
                cancellationToken);
        }
    }
}
