namespace Comuki.Shared.Contracts.Compute;

/// <summary>Why a worker runtime is being stopped/removed.</summary>
public enum ComputeStopReason
{
    /// <summary>Idle pool worker exceeded its TTL.</summary>
    IdleTtl = 0,

    /// <summary>Graceful drain (shutdown, profile/image rollout).</summary>
    Draining = 1,

    /// <summary>Soft stop did not help or the lease is gone — hard kill.</summary>
    Force = 2,

    /// <summary>Lease expired; the work item was requeued elsewhere.</summary>
    LeaseExpired = 3,
}
