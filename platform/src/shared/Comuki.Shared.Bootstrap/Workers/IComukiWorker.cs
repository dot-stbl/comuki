namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// One background worker behind the shared <see cref="ComukiWorkerRegistry"/>
/// loop. The registry owns scheduling, retry, backoff and observability;
/// the implementation only declares its <see cref="Name"/>, its
/// <see cref="Schedule"/> and one unit of work. Register with
/// <c>AddSingleton&lt;IComukiWorker, TWorker&gt;()</c> and the host's
/// <c>AddComukiWorkers()</c> call picks it up.
/// </summary>
public interface IComukiWorker
{
    /// <summary>Unique kebab-case name for observability (e.g. <c>memory-sweep</c>).</summary>
    public string Name { get; }

    /// <summary>How this worker is scheduled.</summary>
    public WorkerSchedule Schedule { get; }

    /// <summary>
    /// One unit of work. Throw only for bugs — a thrown exception and a
    /// failing <see cref="WorkerResult"/> both count as a failure and back
    /// off exponentially; return <c>WorkerResult.Ok</c> for a clean cycle.
    /// </summary>
    /// <param name="context">Scoped services, clock and a per-worker logger for this execution.</param>
    /// <param name="cancellationToken">Cooperative cancellation (host shutdown).</param>
    public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken);
}
