using Comuki.Engine.Compute.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Supervisor;

/// <summary>
/// Scale supervisor behind the comuki worker registry (issue #3 T2.4): one
/// <see cref="ScaleSupervisorCycle.RunAsync"/> pass per
/// <see cref="ScaleSupervisorOptions.PollInterval"/>. The pass reads the
/// work-item backlog through scoped <c>OrchestrationDbContext</c> instances
/// whose global query filters read the ambient subject scope — so the whole
/// pass runs <see cref="ISubjectScopeAccessor.AsSystem"/> (the same contract
/// as the lease reaper and the scheduler dispatcher). A transient provider
/// failure is reported to the registry, which logs it and retries with
/// backoff — the host keeps running.
/// </summary>
/// <param name="cycle">One supervisor pass (scale up/down per profile pool).</param>
/// <param name="scopeAccessor">Ambient scope — the pass runs AsSystem.</param>
/// <param name="scaleOptions">Bound from <c>Compute:Scale</c>; the poll interval drives the schedule.</param>
public sealed class ScaleSupervisorComukiWorker(
    ScaleSupervisorCycle cycle,
    ISubjectScopeAccessor scopeAccessor,
    IOptions<ScaleSupervisorOptions> scaleOptions) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "scale-supervisor";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(scaleOptions.Value.PollInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        using var systemScope = scopeAccessor.AsSystem(Name);

        try
        {
            await cycle.RunAsync(cancellationToken);
            return WorkerResult.Ok("pass complete");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
        {
            return WorkerResult.Fail($"pass failed: {exception.Message}");
        }
    }
}
