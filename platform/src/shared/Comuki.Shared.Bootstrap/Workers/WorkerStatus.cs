namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// Point-in-time observability of one registered worker, served by
/// <see cref="ComukiWorkerRegistry.Snapshot"/>. Null timestamps mean
/// "has not happened yet" (the worker has not run its first cycle).
/// </summary>
/// <param name="Name">The worker's unique name.</param>
/// <param name="LastRunAt">When the last cycle started; null before the first run.</param>
/// <param name="NextRunAt">When the next cycle is scheduled; null while a cycle is in flight or the worker finished.</param>
/// <param name="LastResult">The last cycle's outcome; null before the first run.</param>
/// <param name="ConsecutiveFailures">Failures in a row; drives the exponential backoff.</param>
/// <param name="IsHealthy">True while no consecutive failures are recorded.</param>
public sealed record WorkerStatus(
    string Name,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    WorkerResult? LastResult,
    int ConsecutiveFailures,
    bool IsHealthy);
