using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>
/// The GitHub sync-back port: posts a single status comment with the
/// run link on every terminal transition. For issues the comment is the
/// issue thread; for pull-requests the same endpoint backs the PR
/// conversation (GitHub unifies issue / PR comment threads). On a
/// successful run the port closes the issue, but **never** closes the
/// PR — a Comuki review is a comment, not a decision to merge.
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

        // Comuki does not decide to merge a PR — only the human / repo's
        // branch protection does. Close-on-success applies to issues only.
        if (transition.RunStatus == "Succeeded" && transition.Kind == InboundTicketKind.Issue)
        {
            await api.PatchIssueAsync(parsed.Owner, parsed.Repo, parsed.Number, new GitHubIssueUpdate("closed"), cancellationToken);
        }
    }
}
