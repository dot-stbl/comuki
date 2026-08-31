namespace Comuki.Engine.Compute.Scaling;

/// <summary>Inputs of one <see cref="ScalePolicy.Decide"/> call — snapshot of counts per project+profile.</summary>
/// <param name="QueuedCount">Queued work items of the profile (backlog signal).</param>
/// <param name="IdleCount">Idle (claimable now) workers of the profile.</param>
/// <param name="StaleIdleCount">Idle workers of the profile past the idle TTL.</param>
/// <param name="RunningCount">All running workers of the project (idle + busy, every profile) — the cap denominator.</param>
/// <param name="MinIdle">Warm-idle floor of the project — idle workers are never reaped below it.</param>
/// <param name="MaxConcurrent">Concurrency cap of the project.</param>
public sealed record ScalePolicyInput(
    int QueuedCount,
    int IdleCount,
    int StaleIdleCount,
    int RunningCount,
    int MinIdle,
    int MaxConcurrent);
