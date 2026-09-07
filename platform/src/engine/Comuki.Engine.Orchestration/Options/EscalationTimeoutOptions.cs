using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Orchestration.Options;

/// <summary>
/// Escalation-timeout policy: how long a run can sit in
/// <see cref="Domain.RunStatus.Escalated"/> with no human action before the
/// passive-autonomy ratchet transitions it to <c>Cancelled</c>. Mirrors the
/// shape of <see cref="LeaseOptions"/>: a duration knob, a sweep-cadence
/// knob, and an ops kill-switch. The kill-switch lets an operator disable
/// the ratchet globally (no per-project mode in v1.x).
/// </summary>
public sealed class EscalationTimeoutOptions
{
    /// <summary>Config section: <c>Orchestration:EscalationTimeout</c>.</summary>
    public const string SectionName = "Orchestration:EscalationTimeout";

    /// <summary>How long a run may sit in Escalated before the sweeper auto-archives it.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "1.00:00:00")]
    public TimeSpan EscalationTimeout { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How often <c>EscalationTimeoutWorker</c> sweeps for stale Escalated runs.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Ops kill-switch. False registers the worker but it returns immediately on start.</summary>
    public bool Enabled { get; init; } = true;
}
