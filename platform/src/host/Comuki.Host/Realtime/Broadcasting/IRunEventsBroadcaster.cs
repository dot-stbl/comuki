using Comuki.Shared.Contracts.Journal;

namespace Comuki.Host.Realtime.Broadcasting;

/// <summary>
/// Fan-out of journal appends to the SignalR groups: every entry goes to
/// its <c>run:{id}</c> group, attention-worthy transitions additionally to
/// the <c>project:{id}:attention</c> group. Host-local port on purpose —
/// only the host's broadcast interceptor consumes it, so it stays out of
/// Shared.Contracts.
/// </summary>
public interface IRunEventsBroadcaster
{
    /// <summary>
    /// Broadcasts a batch of journal entries (one SaveChanges worth).
    /// Best-effort by contract: failures are logged and swallowed — a
    /// broadcast problem must never fail the writer that produced the rows.
    /// </summary>
    /// <param name="entries"></param>
    /// <param name="cancellationToken"></param>
    public Task BroadcastAsync(IReadOnlyList<RunEventEntry> entries, CancellationToken cancellationToken = default);
}
