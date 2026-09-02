using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Realtime.Reading;

/// <summary>
/// RunId → ProjectId lookup for the realtime surface: join checks need the
/// run's project to evaluate the object axis, and attention broadcasts need
/// it to address the project group. Host-local port on purpose — only the
/// hub and the broadcaster consume it, so it stays out of Shared.Contracts.
/// </summary>
public interface IRealtimeRunProjects
{
    /// <summary>
    /// Resolves the owning project of every given run; unknown run ids are
    /// simply absent from the result.
    /// </summary>
    /// <param name="runIds"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyDictionary<RunId, ProjectId>> ReadAsync(
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken = default);
}
