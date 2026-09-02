using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Costs;

/// <summary>
/// Host budget gate: cancels a non-terminal run and appends
/// <see cref="RunEventTypes.BudgetExceeded"/> to the journal. Scoped over
/// the orchestration context.
/// </summary>
/// <param name="db"></param>
/// <param name="journal"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class OrchestrationBudgetGate(
    OrchestrationDbContext db,
    IRunJournal journal,
    TimeProvider clock,
    ILogger<OrchestrationBudgetGate> logger) : IBudgetGate
{
    /// <inheritdoc />
    public async Task HardStopAsync(
        RunId runId,
        ProjectId projectId,
        long spentUsdMicros,
        long hardLimitUsdMicros,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = await db.Runs.FirstOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Budget hard-stop skipped: run {RunId} not found", runId);
            return;
        }

        if (run.Status is RunStatus.Succeeded or RunStatus.Cancelled)
        {
            return;
        }

        var from = run.Status.ToString();
        run.TransitionTo(RunStatus.Cancelled, now);
        await db.SaveChangesAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(
            new
            {
                projectId = projectId.Value,
                spentUsdMicros,
                hardLimitUsdMicros,
                from,
                to = nameof(RunStatus.Cancelled),
            },
            JsonSerializerOptions.Web);

        await journal.AppendAsync(
            new RunEventEntry(Guid.CreateVersion7(), runId, RunEventTypes.BudgetExceeded, payload, now),
            cancellationToken);

        logger.LogWarning(
            "Run {RunId} cancelled: project {ProjectId} hard budget exceeded ({SpentUsdMicros}/{HardLimitUsdMicros})",
            runId,
            projectId,
            spentUsdMicros,
            hardLimitUsdMicros);
    }
}
