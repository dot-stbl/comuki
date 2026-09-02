using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// The GitHub sync-back port: posts a status comment with the run link
/// on every terminal transition and closes the issue when the run
/// succeeded. Repeats are tolerated — the tracker-side comment may
/// duplicate, but the state patch is idempotent.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class GitHubTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : ITicketSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.GitHub;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, TicketTransition transition, CancellationToken cancellationToken = default)
    {
        if (GitHubPayloadMapper.ParseExternalId(transition.ExternalId) is not { } parsed)
        {
            throw new InvalidOperationException($"github external id '{transition.ExternalId}' is malformed");
        }

        var settings = GitHubSettings.Parse(connection.SettingsJson);
        var api = clients.GitHub(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv));

        await api.PostCommentAsync(
            parsed.Owner,
            parsed.Repo,
            parsed.Number,
            new GitHubCommentBody(TrackerSyncComments.Of(transition)),
            cancellationToken);

        if (transition.RunStatus == "Succeeded")
        {
            await api.PatchIssueAsync(parsed.Owner, parsed.Repo, parsed.Number, new GitHubIssueUpdate("closed"), cancellationToken);
        }
    }
}
