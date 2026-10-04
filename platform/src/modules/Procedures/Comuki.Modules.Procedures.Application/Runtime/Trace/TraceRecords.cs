namespace Comuki.Modules.Procedures.Application.Runtime.Trace;

/// <summary>One event in the procedure trace — the replay timeline.</summary>
/// <param name="NodeId">The graph node this event relates to.</param>
/// <param name="EventType">planned, started, completed, failed, gate_opened, gate_resolved, generation_opened, generation_exhausted, late_result, pin_recorded.</param>
/// <param name="Detail">Human-readable detail for the replay timeline.</param>
/// <param name="At">When the event was recorded (UTC).</param>
public sealed record TraceEvent(
    string NodeId,
    string EventType,
    string Detail,
    DateTimeOffset At);

/// <summary>
/// The full trace of a procedure-pinned run: every event in order,
/// against the pinned version. Replay reads this to distinguish the
/// compiled plan from the observed execution (spec requirement
/// "Planned versus observed replay").
/// </summary>
/// <param name="PinnedVersionId">The version the run pinned at admission.</param>
/// <param name="ProcedureKey">The procedure the run rides; surfaced by the live-projection endpoint.</param>
/// <param name="ProjectId">The project that owns the procedure; surfaced by the live-projection endpoint.</param>
/// <param name="PinnedAt">When the pin was recorded (UTC); the first event's timestamp.</param>
/// <param name="Events">All trace events in chronological order.</param>
public sealed record ProcedureTrace(
    string PinnedVersionId,
    string ProcedureKey,
    Guid ProjectId,
    DateTimeOffset PinnedAt,
    IReadOnlyList<TraceEvent> Events);

/// <summary>The drift classification between planned and observed graphs.</summary>
/// <param name="IsWithinPolicy">True when the observed drift stays inside the declared tolerance.</param>
/// <param name="Summary">Human-readable summary of the drift.</param>
public sealed record DriftClassification(bool IsWithinPolicy, string Summary);
