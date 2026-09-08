using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Costs;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Contracts.Usage;
using Comuki.Shared.Kernel.Exceptions;
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
/// <param name="budgets"></param>
/// <param name="usageEvents"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class OrchestrationBudgetGate(
    OrchestrationDbContext db,
    IRunJournal journal,
    IProjectBudgetSettings budgets,
    IUsageEventStore usageEvents,
    TimeProvider clock,
    ILogger<OrchestrationBudgetGate> logger) : IBudgetGate
{
    /// <summary>Stable code for the hard-cap denial surfaced to ProblemDetails.</summary>
    public const string HardExceededCode = "budget.hard_exceeded";

    /// <summary>Stable code for the soft-cap advisory event (logged, not thrown).</summary>
    public const string SoftExceededCode = "budget.soft_exceeded";

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

    /// <inheritdoc />
    public async Task EnforceClaimAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var caps = await budgets.GetAsync(projectId, cancellationToken);
        var spent = await usageEvents.SumProjectCostUsdMicrosAsync(projectId, cancellationToken: cancellationToken);

        if (caps.HardLimitUsdMicros is { } hard && spent >= hard)
        {
            logger.LogError(
                "Project {ProjectId} hard budget exceeded at claim time: spent={SpentUsdMicros} hard={HardLimitUsdMicros}",
                projectId,
                spent,
                hard);

            throw new BudgetExceededException(
                HardExceededCode,
                "project hard budget exceeded; claim denied",
                inner: null);
        }

        if (caps.SoftLimitUsdMicros is { } soft && spent >= soft)
        {
            logger.LogWarning(
                "Project {ProjectId} soft budget exceeded at claim time: spent={SpentUsdMicros} soft={SoftLimitUsdMicros} (claim allowed)",
                projectId,
                spent,
                soft);
        }
    }
}
