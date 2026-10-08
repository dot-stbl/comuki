using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Integrations.Application.Ports.Admission;

/// <summary>
/// Read-side port over the orchestration runs — how the run status
/// bridge learns that a claimed ticket's run reached a terminal status
/// without the intake module referencing the engine. Implemented by the
/// host over the orchestration context.
/// </summary>
public interface IRunStatusReader
{
    /// <summary>Reads the current status names (PascalCase) of the given runs; missing runs are absent.</summary>
    /// <param name="runIds"></param>
    /// <param name="cancellationToken"></param>
    /// 
    public Task<IReadOnlyDictionary<RunId, string>> ReadStatusesAsync(
        IReadOnlyCollection<RunId> runIds,
        CancellationToken cancellationToken = default);
}
