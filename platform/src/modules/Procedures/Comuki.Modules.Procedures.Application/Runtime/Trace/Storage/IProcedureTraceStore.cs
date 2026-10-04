namespace Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;

/// <summary>
/// Application-layer seam for the planned-vs-observed trace the spec
/// pins ("Planned versus observed replay", task 5.3). The trace is
/// keyed by <c>runId</c> — the platform routes "what did this run
/// actually do?" by the run, not by the procedure. Two runs of the
/// same procedure carry independent traces.
/// 
/// <para>
/// The recorder (<see cref="ProcedureTraceRecorder"/>) stays a static
/// pure-data transformer (graph → drift classification, etc.); the
/// store is the durable end. An in-memory implementation is enough for
/// the host today; an EF implementation lands with task 5.3's
/// planned-vs-observed trace persistence. The seam keeps callers from
/// caring which store is wired.
/// </para>
/// </summary>
public interface IProcedureTraceStore
{
    /// <summary>
    /// Seeds a fresh trace for <paramref name="runId"/> with the run's
    /// pin metadata. The first call to this method for a given runId
    /// wins — repeated calls on the same runId are no-ops. Subsequent
    /// <see cref="RecordAsync"/> calls append events immutably.
    /// </summary>
    /// <param name="runId">The run the trace belongs to.</param>
    /// <param name="versionId">The content-addressed version the run pinned.</param>
    /// <param name="procedureKey">The procedure the run rides.</param>
    /// <param name="projectId">The project that owns the procedure.</param>
    /// <param name="pinnedAt">When the pin was recorded (UTC).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task SeedAsync(
        Guid runId,
        string versionId,
        string procedureKey,
        Guid projectId,
        DateTimeOffset pinnedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a trace event against <paramref name="runId"/>'s trace.
    /// The seed must have run first; calls before seeding are no-ops
    /// (the store does not synthesise a seed from a single event).
    /// </summary>
    /// <param name="runId">The run the trace belongs to.</param>
    /// <param name="traceEvent">The trace event to append.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task RecordAsync(
        Guid runId,
        TraceEvent traceEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full trace for <paramref name="runId"/>, or null when
    /// no events have been recorded yet. Order matches the order
    /// events were recorded (chronological).
    /// </summary>
    /// <param name="runId">The run whose trace to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<ProcedureTrace?> ReadAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
