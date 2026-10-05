using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Harness;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs;

/// <summary>
/// Default <see cref="IRunHarnessResolver"/>: joins <c>runs</c> to
/// <c>work_items</c> in the orchestration schema, picks the single
/// running work item for the run, and looks up the harness by profile
/// key in the in-process <see cref="HarnessRegistry"/>. The schema
/// read is the same one <see cref="ExecutionIdResolver"/> does; the
/// <see cref="HarnessRegistry"/> lookup is a single
/// <c>ConcurrentDictionary</c> read — the resolver's cost is
/// dominated by the EF round-trip, not the harness map.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="registry">In-process harness catalog; populated at
/// host boot from the registered <see cref="IHarness"/> instances.</param>
public sealed class RunHarnessResolver(
    OrchestrationDbContext db,
    HarnessRegistry registry) : IRunHarnessResolver
{
    /// <inheritdoc />
    public async Task<IHarness?> ResolveAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        // Mirror the ExecutionIdResolver's "one running item per run"
        // posture: a single Running work item under the run is the
        // live harness surface; two Running items is state corruption
        // and the resolver returns null (the operator endpoint
        // surfaces it as the same "no live execution" outcome the
        // ExecutionIdResolver returns).
        var profileKey = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId
                && item.Status == WorkItemStatus.Running
                && item.LeasedBy != null)
            .Select(item => item.ProfileKey)
            .FirstOrDefaultAsync(cancellationToken);

        return profileKey is null
            ? null
            : registry.FindByProfileKey(profileKey);
    }
}
