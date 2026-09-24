using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Verify.Application.Ports;

/// <summary>
/// Persistence port for <see cref="GenericCommandRun"/>. Mirrors the
/// scheduler store shape: every CRUD verb plus the <c>FOR UPDATE SKIP
/// LOCKED</c> due-run query the worker polls against. The host never
/// reaches into the verify schema — implementations live in
/// <c>Comuki.Modules.Verify.Infrastructure</c>.
/// </summary>
public interface IGenericCommandStore
{
    /// <summary>Inserts a new run (always Pending).</summary>
    /// <param name="run">The run to insert.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task AddAsync(GenericCommandRun run, CancellationToken cancellationToken = default);

    /// <summary>Lookup by id.</summary>
    /// <param name="runId">The run id to look up.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<GenericCommandRun?> FindAsync(GenericCommandRunId runId, CancellationToken cancellationToken = default);

    /// <summary>Lists the most recent runs of one project; newest first.</summary>
    /// <param name="projectId">Owning project.</param>
    /// <param name="limit">Hard cap on rows returned.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<IReadOnlyList<GenericCommandRun>> ListAsync(
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated run (status / output_log / timestamps).</summary>
    /// <param name="run">The run to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task UpdateAsync(GenericCommandRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Pending runs the worker can claim, locked with
    /// <c>FOR UPDATE SKIP LOCKED</c> so two host replicas never run the
    /// same row twice on the same tick.
    /// </summary>
    /// <param name="limit">Per-cycle batch cap.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<IReadOnlyList<GenericCommandRun>> ClaimPendingAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
