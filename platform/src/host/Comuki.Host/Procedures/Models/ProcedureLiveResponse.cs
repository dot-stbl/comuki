using Comuki.Modules.Procedures.Application.Runtime.Trace;

namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for <c>GET /api/v1/procedures/runs/{runId}/live</c> —
/// the dashboard's Live run mode reads this to render the pinned-version
/// chrome, the path, and the drift verdict. Composed server-side from
/// the run's planned-vs-observed trace; the trace carries the pin
/// metadata (version, procedure key, project id, pinned at) seeded at
/// admission, so the projection is a single read. Null fields render
/// at an empty state in Studio, not an HTTP error.
/// </summary>
/// <param name="RunId">The run the projection belongs to.</param>
/// <param name="PinnedVersionId">
/// The content-addressed version id the run was bound to, or null when the
/// run never admitted against a procedure (unpinned default).
/// </param>
/// <param name="ProcedureKey">
/// Stable key of the procedure the run rides, or null when unpinned.
/// </param>
/// <param name="ProjectId">Project that owns the procedure.</param>
/// <param name="PinnedAt">When the pin was recorded (UTC, ISO 8601).</param>
/// <param name="EventCount">How many events the trace carries.</param>
/// <param name="Events">The trace's events (chronological) — server-side composed.</param>
/// <param name="Drift">Drift verdict between the compiled plan and the observed execution, or null.</param>
public sealed record ProcedureLiveResponse(
    Guid RunId,
    string? PinnedVersionId,
    string? ProcedureKey,
    Guid? ProjectId,
    DateTimeOffset? PinnedAt,
    int EventCount,
    IReadOnlyList<ProcedureTraceResponse.TraceEventDto> Events,
    ProcedureLiveResponse.DriftDto? Drift)
{
    /// <summary>Drift verdict — same shape ProcedureTraceRecorder.ClassifyDrift produces.</summary>
    /// <param name="IsWithinPolicy">True when the observed drift stayed inside the declared tolerance.</param>
    /// <param name="Summary">Human-readable summary.</param>
    public sealed record DriftDto(bool IsWithinPolicy, string Summary);

    /// <summary>Builds the live projection from the run's trace only — the trace
    /// carries the pin metadata (version, procedure key, project id, pinned
    /// at) seeded at admission time, so the projection is a single read.</summary>
    /// <param name="runId">The run the projection belongs to.</param>
    /// <param name="trace">The full trace the store returned (null when no events).</param>
    /// <param name="drift">Drift verdict (null when no planned-vs-observed comparison has been recorded yet).</param>
    public static ProcedureLiveResponse From(
        Guid runId,
        ProcedureTrace? trace,
        DriftClassification? drift = null)
    {
        if (trace is null)
        {
            return new ProcedureLiveResponse(
                RunId: runId,
                PinnedVersionId: null,
                ProcedureKey: null,
                ProjectId: null,
                PinnedAt: null,
                EventCount: 0,
                Events: [],
                Drift: drift is null
                    ? null
                    : new DriftDto(drift.IsWithinPolicy, drift.Summary));
        }

        var events = trace.Events
            .Select(static traceEvent => new ProcedureTraceResponse.TraceEventDto(
                traceEvent.NodeId,
                traceEvent.EventType,
                traceEvent.Detail,
                traceEvent.At))
            .ToList();
        return new ProcedureLiveResponse(
            RunId: runId,
            PinnedVersionId: trace.PinnedVersionId,
            ProcedureKey: trace.ProcedureKey,
            ProjectId: trace.ProjectId,
            PinnedAt: trace.PinnedAt,
            EventCount: events.Count,
            Events: events,
            Drift: drift is null
                ? null
                : new DriftDto(drift.IsWithinPolicy, drift.Summary));
    }
}
