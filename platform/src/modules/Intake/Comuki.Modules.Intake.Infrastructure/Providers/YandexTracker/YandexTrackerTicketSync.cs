using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// The Yandex Tracker sync-back port: a status comment with the run
/// link on every terminal transition and the configured resolved
/// transition when the run succeeded.
/// </summary>
/// <param name="clients"></param>
/// <param name="secrets"></param>
public sealed class YandexTrackerTicketSync(
    TrackerClientFactory clients,
    ISecretResolver secrets) : ITicketSyncPort
{
    /// <inheritdoc />
    public string SourceKey => TicketProviderKeys.YandexTracker;

    /// <inheritdoc />
    public async Task TransitionAsync(SourceConnection connection, TicketTransition transition, CancellationToken cancellationToken = default)
    {
        var settings = YandexTrackerSettings.Parse(connection.SettingsJson);
        var api = clients.YandexTracker(settings.ApiBase, secrets.Resolve(settings.ApiTokenEnv), settings.OrgId);

        await api.PostCommentAsync(transition.ExternalId, new TrackerCommentBody(TrackerSyncComments.Of(transition)), cancellationToken);

        if (transition.RunStatus == "Succeeded")
        {
            await api.TransitionAsync(transition.ExternalId, settings.ResolvedTransition, cancellationToken);
        }
    }
}
