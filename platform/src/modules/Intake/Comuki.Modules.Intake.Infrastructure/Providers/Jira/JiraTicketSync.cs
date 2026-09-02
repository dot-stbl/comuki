using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// The Jira sync-back port: a status comment with the run link on
/// every terminal transition and the configured resolved transition
/// when the run succeeded.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class JiraTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : ITicketSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.Jira;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, TicketTransition transition, CancellationToken cancellationToken = default)
    {
        var settings = JiraSettings.Parse(connection.SettingsJson);
        var api = clients.Jira(settings.Site, secrets.Resolve(settings.ApiTokenEnv));

        await api.PostCommentAsync(transition.ExternalId, new JiraCommentBody(TrackerSyncComments.Of(transition)), cancellationToken);

        if (transition.RunStatus == "Succeeded" && settings.ResolvedTransitionId is { Length: > 0 } transitionId)
        {
            await api.TransitionAsync(
                transition.ExternalId,
                new JiraTransitionBody(new JiraTransitionRef(transitionId)),
                cancellationToken);
        }
    }
}
