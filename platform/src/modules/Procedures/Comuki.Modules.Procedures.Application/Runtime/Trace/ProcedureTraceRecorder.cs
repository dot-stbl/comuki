using Comuki.Modules.Procedures.Application.Runtime.Helpers;
using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Modules.Procedures.Application.Runtime.Trace;

/// <summary>
/// Records trace events immutably and classifies drift between the
/// planned graph and the observed execution. The trace is the replay
/// timeline: planned vs observed, with generations, gate decisions,
/// and dynamic work items visible (spec: "Record the planned-vs-observed
/// trace"). The width metric lives in
/// <see cref="GraphWidth"/>.
/// </summary>
public static class ProcedureTraceRecorder
{
    /// <summary>Starts a new trace for the given pinned version.</summary>
    /// <param name="pinnedVersionId">The version the run pinned.</param>
    /// <param name="procedureKey">The procedure the run rides.</param>
    /// <param name="projectId">The project that owns the procedure.</param>
    /// <param name="pinnedAt">When the pin was recorded (UTC).</param>
    /// <returns>An empty trace.</returns>
    public static ProcedureTrace Record(
        string pinnedVersionId,
        string procedureKey,
        Guid projectId,
        DateTimeOffset pinnedAt)
    {
        return new ProcedureTrace(pinnedVersionId, procedureKey, projectId, pinnedAt, []);
    }

    /// <summary>Appends an event immutably — returns a new trace.</summary>
    /// <param name="trace">The current trace.</param>
    /// <param name="newEvent">The event to append.</param>
    /// <returns>A new trace with the event appended.</returns>
    public static ProcedureTrace Append(ProcedureTrace trace, TraceEvent newEvent)
    {
        return trace with { Events = [.. trace.Events, newEvent] };
    }

    /// <summary>
    /// Classifies whether the observed graph's drift from the planned
    /// graph stays within the declared fan-out range. The comparison is
    /// width-based: how many nodes sit at each topological depth. If the
    /// observed max-width exceeds the planned max-width plus the declared
    /// range, the drift is outside policy.
    /// </summary>
    /// <param name="planned">The compiled plan graph.</param>
    /// <param name="observed">The actually-executed graph.</param>
    /// <param name="declaredFanOutRange">The tolerance the procedure declared.</param>
    /// <returns>The drift classification.</returns>
    public static DriftClassification ClassifyDrift(
        ProcedureGraph planned,
        ProcedureGraph observed,
        int declaredFanOutRange)
    {
        var plannedWidth = GraphWidth.MaxWidth(planned);
        var observedWidth = GraphWidth.MaxWidth(observed);
        var isWithin = observedWidth <= plannedWidth + declaredFanOutRange;

        return new DriftClassification(
            isWithin,
            $"Planned max-width {plannedWidth}, observed {observedWidth}, tolerance {declaredFanOutRange}: {(isWithin ? "within policy" : "outside policy")}.");
    }
}
