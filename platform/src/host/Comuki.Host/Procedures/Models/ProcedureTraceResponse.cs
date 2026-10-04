using Comuki.Modules.Procedures.Application.Runtime.Trace;

namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for <c>GET /api/v1/procedures/runs/{runId}/trace</c> —
/// the planned-vs-observed timeline for one procedure-pinned run.
/// Read by Studio's Replay panel ("planned vs observed" drift
/// classification, spec: "Planned versus observed replay", task 5.3).
/// </summary>
/// <param name="RunId">The run the trace belongs to.</param>
/// <param name="PinnedVersionId">The content-addressed version the run pinned.</param>
/// <param name="Events">The trace's events in chronological order.</param>
public sealed record ProcedureTraceResponse(
    Guid RunId,
    string PinnedVersionId,
    IReadOnlyList<ProcedureTraceResponse.TraceEventDto> Events)
{
    /// <summary>One trace event in the wire shape.</summary>
    /// <param name="NodeId">The graph node (or version id for a pin event) the event relates to.</param>
    /// <param name="EventType">planned / started / completed / failed / gate_opened / gate_resolved / generation_opened / generation_exhausted / late_result / pin_recorded.</param>
    /// <param name="Detail">Human-readable detail.</param>
    /// <param name="At">When the event was recorded (UTC, ISO 8601).</param>
    public sealed record TraceEventDto(
        string NodeId,
        string EventType,
        string Detail,
        DateTimeOffset At);

    /// <summary>Maps from the Application record to the wire shape.</summary>
    public static ProcedureTraceResponse From(Guid runId, ProcedureTrace trace)
    {
        var events = trace.Events
            .Select(static traceEvent => new TraceEventDto(
                traceEvent.NodeId,
                traceEvent.EventType,
                traceEvent.Detail,
                traceEvent.At))
            .ToList();
        return new ProcedureTraceResponse(runId, trace.PinnedVersionId, events);
    }
}
