using Comuki.Modules.Integrations.Application.Ports.Sync;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Integrations.Infrastructure.Providers.GitLab;

/// <summary>
/// The GitLab sync-back port: a single status note with the run link on
/// every terminal transition, the close state event when the run
/// succeeded. Close-on-success applies to issues only — a Comuki
/// review on an MR is a comment, not a merge decision.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class GitLabTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : IIntegrationSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.GitLab;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, InboundItemTransition transition, CancellationToken cancellationToken = default)
    {
        var hashIndex = transition.ExternalId.IndexOf('#');
        if (hashIndex <= 0 || !int.TryParse(transition.ExternalId[(hashIndex + 1)..], out var iid))
        {
            throw new InvalidOperationException($"gitlab external id '{transition.ExternalId}' is malformed");
        }

        var settings = GitLabSettings.Parse(connection.SettingsJson);
        var api = clients.GitLab(
            settings.ApiBase,
            await secrets.ResolveAsync(settings.ApiTokenEnv, cancellationToken));

        var body = new GitLabNoteBody(TrackerSyncComments.Of(transition));

        if (transition.Kind == InboundItemKind.PullRequest)
        {
            await api.PostMergeRequestNoteAsync(settings.ProjectId, iid, body, cancellationToken);
        }
        else
        {
            await api.PostNoteAsync(settings.ProjectId, iid, body, cancellationToken);
        }

        // Comuki does not decide to merge an MR — close-on-success applies to issues only.
        if (transition.RunStatus == RunStatuses.Succeeded && transition.Kind == InboundItemKind.Issue)
        {
            await api.UpdateIssueAsync(settings.ProjectId, iid, new GitLabIssueUpdate("close"), cancellationToken);
        }
    }
}
