using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Orchestration.Options;

/// <summary>
/// Claim/lease policy: how long a lease runs, how often the reaper sweeps,
/// the grace window past expiry before a lease is reaped, and the retry
/// budget before a stalled item is failed.
/// </summary>
public sealed class LeaseOptions
{
    /// <summary>Config section: <c>Orchestration:Lease</c>.</summary>
    public const string SectionName = "Orchestration:Lease";

    /// <summary>Lease duration handed out on claim / extended by heartbeat.</summary>
    [Range(typeof(TimeSpan), "00:00:15", "1.00:00:00")]
    public TimeSpan LeaseTtl { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often <c>LeaseReaperWorker</c> sweeps for expired leases.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan ReapInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Buffer past <c>lease_until</c> before the reaper acts — absorbs clock skew and slow workers.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00")]
    public TimeSpan ReapGrace { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Claim attempts (initial + requeues) before the reaper fails the item instead of requeueing.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; init; } = 3;
}
