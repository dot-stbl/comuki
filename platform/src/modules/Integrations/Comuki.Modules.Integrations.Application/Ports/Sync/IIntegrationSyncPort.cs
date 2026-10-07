using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Domain.Connections;

namespace Comuki.Modules.Integrations.Application.Ports.Sync;

/// <summary>
/// The sync-back port: pushes a finished run's status into the tracker
/// (a status comment with the run link, plus the close/resolve state
/// change on success). Implementations share the source provider's
/// Refit client and connection settings parser.
/// </summary>
public interface IIntegrationSyncPort
{
    /// <summary>Kebab-case source key this port serves.</summary>
    public string SourceKey { get; }

    /// <summary>Applies the transition; idempotent — providers tolerate repeats.</summary>
    /// <param name="connection"></param>
    /// <param name="transition"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task TransitionAsync(
        SourceConnection connection,
        InboundItemTransition transition,
        CancellationToken cancellationToken = default);
}
