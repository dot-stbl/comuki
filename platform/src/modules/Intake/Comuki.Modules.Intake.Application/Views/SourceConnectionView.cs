using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Application.Views;

/// <summary>
/// Read-model of a source connection. Settings and secret env NAMES are
/// returned (never secret values) plus the hook path to paste into the
/// tracker's webhook settings.
/// </summary>
/// <param name="Id"></param>
/// <param name="ProjectId"></param>
/// <param name="Provider">Kebab-case provider key.</param>
/// <param name="Name"></param>
/// <param name="SettingsJson"></param>
/// <param name="SecretEnvRef"></param>
/// <param name="WebhookPath">Hook route to configure in the tracker (relative).</param>
/// <param name="Enabled"></param>
public sealed record SourceConnectionView(
    Guid Id,
    Guid ProjectId,
    string Provider,
    string Name,
    string SettingsJson,
    string SecretEnvRef,
    string WebhookPath,
    bool Enabled)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="connection"></param>
    /// <returns></returns>
    public static SourceConnectionView Of(SourceConnection connection)
    {
        return new SourceConnectionView(
            connection.Id.Value,
            connection.ProjectId.Value,
            TicketProviderKeys.Key(connection.Provider),
            connection.Name,
            connection.SettingsJson,
            connection.SecretEnvRef,
            $"/api/hooks/{TicketProviderKeys.Key(connection.Provider)}/{connection.WebhookKey}",
            connection.Enabled);
    }
}
