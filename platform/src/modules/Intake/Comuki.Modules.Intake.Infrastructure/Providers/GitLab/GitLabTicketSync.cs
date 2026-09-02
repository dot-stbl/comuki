using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// The GitLab sync-back port: a status note with the run link on every
/// terminal transition, the close state event when the run succeeded.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class GitLabTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : ITicketSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.GitLab;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, TicketTransition transition, CancellationToken cancellationToken = default)
    {
        var hashIndex = transition.ExternalId.IndexOf('#');
        if (hashIndex <= 0 || !int.TryParse(transition.ExternalId[(hashIndex + 1)..], out var issueIid))
        {
            throw new InvalidOperationException($"gitlab external id '{transition.ExternalId}' is malformed");
        }

        var settings = GitLabSettings.Parse(connection.SettingsJson);
        var api = clients.GitLab(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv));

        await api.PostNoteAsync(settings.ProjectId, issueIid, new GitLabNoteBody(TrackerSyncComments.Of(transition)), cancellationToken);

        if (transition.RunStatus == "Succeeded")
        {
            await api.UpdateIssueAsync(settings.ProjectId, issueIid, new GitLabIssueUpdate("close"), cancellationToken);
        }
    }
}
