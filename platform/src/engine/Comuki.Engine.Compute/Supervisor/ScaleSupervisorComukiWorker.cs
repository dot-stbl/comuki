using Comuki.Engine.Compute.Options;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Supervisor;

/// <summary>
/// Scale supervisor behind the comuki worker registry (issue #3 T2.4): one
/// <see cref="ScaleSupervisorCycle.RunAsync"/> pass per
/// <see cref="ScaleSupervisorOptions.PollInterval"/>. A transient provider
/// failure is reported to the registry, which logs it and retries with
/// backoff — the host keeps running.
/// </summary>
/// <param name="cycle">One supervisor pass (scale up/down per profile pool).</param>
/// <param name="scaleOptions">Bound from <c>Compute:Scale</c>; the poll interval drives the schedule.</param>
public sealed class ScaleSupervisorComukiWorker(
    ScaleSupervisorCycle cycle,
    IOptions<ScaleSupervisorOptions> scaleOptions) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "scale-supervisor";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(scaleOptions.Value.PollInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
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
