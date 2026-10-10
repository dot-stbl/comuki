using System.Collections.Concurrent;

namespace Comuki.Modules.Procedures.Application.Runtime.Trace.Storage;

/// <summary>
/// In-memory <see cref="IProcedureTraceStore"/> — the host wires this
/// until task 5.3 lands EF persistence for the planned-vs-observed
/// trace. Singleton-scoped; the in-memory map survives the lifetime of
/// the host process. The admission binder seeds the run's trace at
/// admission with the pin metadata, then appends trace events as the
/// runtime drives the procedure (repair generations, gate decisions,
/// late results).
/// </summary>
public sealed class InMemoryProcedureTraceStore : IProcedureTraceStore
{
    private readonly ConcurrentDictionary<Guid, ProcedureTrace> traces = new();

    /// <inheritdoc />
    public Task SeedAsync(
        Guid runId,
        string versionId,
        string procedureKey,
        Guid projectId,
        DateTimeOffset pinnedAt,
        CancellationToken cancellationToken = default)
    {
        // The first event of a trace is the synthetic pin_recorded stamp
        // — operators see "this run was bound to vN at time T" without
        // having to wait for the next runtime event. PinnedVersionId on
        // the ProcedureTrace is the same id, kept in lockstep.
        var pinEvent = new TraceEvent(
            NodeId: versionId,
            EventType: "pin_recorded",
            Detail: $"project {projectId} procedure {procedureKey} pinned to {versionId}",
            At: pinnedAt);
        traces.AddOrUpdate(
            runId,
            _ => new ProcedureTrace(versionId, [pinEvent]),
            (_, current) => current);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordAsync(
        Guid runId,
        TraceEvent traceEvent,
        CancellationToken cancellationToken = default)
    {
        traces.AddOrUpdate(
            runId,
            // A RecordAsync call without a prior SeedAsync would otherwise
            // synthesise a trace from the event alone — keep the recorder
            // honest by skipping the no-op when there is no seeded trace.
            // The admission binder is the only path that creates traces
            // today; new callers should Seed first.
            _ => throw new InvalidOperationException(
                $"Trace for run {runId} has not been seeded; call SeedAsync before RecordAsync."),
            (_, current) => ProcedureTraceRecorder.Append(current, traceEvent));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ProcedureTrace?> ReadAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(traces.TryGetValue(runId, out var trace) ? trace : null);
    }
}
