using Comuki.Modules.Intake.Domain.Connections;

namespace Comuki.Modules.Intake.Application.Ports;

/// <summary>
/// The sync-back port: pushes a finished run's status into the tracker
/// (a status comment with the run link, plus the close/resolve state
/// change on success). Implementations share the source provider's
/// Refit client and connection settings parser.
/// </summary>
public interface ITicketSyncPort
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
        TicketTransition transition,
        CancellationToken cancellationToken = default);
}
