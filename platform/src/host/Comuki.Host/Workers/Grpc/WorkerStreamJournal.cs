using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Grpc;

/// <summary>
/// Per-stream journal writer: remembers the RunId binding from the first
/// <see cref="StageStart"/> and appends every worker event to the run
/// timeline as a <see cref="RunEventTypes.WorkerReported"/> entry whose
/// payload mirrors the stage record. The verify event variant
/// (<see cref="StageVerify"/>) lands on the timeline as
/// <c>verify.completed</c> / <c>verify.failed</c> with a structured
/// payload of its own — see <see cref="WorkerStreamJournalMapping.ToEntry"/>.
/// Events before a Start (or with an unparsable binding) are dropped with
/// a warning — the protocol guarantees Start first.
/// </summary>
/// <param name="journal"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class WorkerStreamJournal(
    IRunJournal journal,
    TimeProvider clock,
    ILogger<WorkerStreamJournal> logger)
{
    /// <summary>
    /// boundary: mutated only by the binding helper on the first StageStart
    /// </summary>
    private RunId? runId;

    /// <summary>The run the stream is bound to; null until a Start was seen.</summary>
    public RunId? RunId => runId;

    /// <summary>Appends one worker event to the timeline, binding on the Start record.</summary>
    /// <param name="workerEvent"></param>
    /// <param name="cancellationToken"></param>
    public async Task AppendAsync(WorkerEvent workerEvent, CancellationToken cancellationToken)
    {
        if (WorkerStreamJournalBinding.Resolve(ref runId, workerEvent) is not { } owner)
        {
            logger.LogWarning("Dropping worker event with no StageStart binding");
            return;
        }

        var entry = WorkerStreamJournalMapping.ToEntry(owner, workerEvent, clock.GetUtcNow());
        if (entry is null)
        {
            return;
        }

        await journal.AppendAsync(entry, cancellationToken);
    }
}

/// <summary>Resolves the run binding of a stream from its events; null until a parsable Start arrives.</summary>
internal static class WorkerStreamJournalBinding
{
    public static RunId? Resolve(ref RunId? bound, WorkerEvent workerEvent)
    {
        if (workerEvent.Start is not { } start)
        {
            return bound;
        }

        if (!Guid.TryParse(start.RunId, out var runGuid))
        {
            return null;
        }

        bound = new RunId(runGuid);
        return bound;
    }
}

/// <summary>Pure mapping of worker events to journal entries; null for events with no stage payload.</summary>
internal static class WorkerStreamJournalMapping
{
    /// <summary>Journal type of every non-verify worker-reported event.</summary>
    public const string JournalType = RunEventTypes.WorkerReported;

    /// <summary>Status string on <see cref="StageVerify"/> that maps to <c>verify.completed</c>.</summary>
    public const string VerifyCompletedStatus = "completed";

    /// <summary>Status string on <see cref="StageVerify"/> that maps to <c>verify.failed</c>.</summary>
    public const string VerifyFailedStatus = "failed";

    public static RunEventEntry? ToEntry(RunId owner, WorkerEvent workerEvent, DateTimeOffset occurredAt)
    {
        // Verify events get their own journal type with a payload tailored
        // to the verify scenario (opcode list / opcode+exit-code). Start /
        // Activity / Report keep the legacy worker.reported shape.
        if (workerEvent.Verify is { } verify)
        {
            var type = verify.Status switch
            {
                VerifyCompletedStatus => RunEventTypes.VerifyCompleted,
                VerifyFailedStatus => RunEventTypes.VerifyFailed,
                _ => RunEventTypes.WorkerReported,
            };

            var payload = JsonSerializer.Serialize(
                new VerifyJournalPayload(
                    WorkItemId: verify.WorkItemId,
                    Opcodes: verify.Opcodes,
                    FailedOpcode: verify.FailedOpcode,
                    ExitCode: verify.ExitCode,
                    Reason: verify.Reason),
                JsonSerializerOptions.Web);

            return new RunEventEntry(Guid.NewGuid(), owner, type, payload, occurredAt);
        }

        // Condition events (harden-pi-worker-sandbox 5.1, spec D6) map
        // to a worker.condition journal entry with { name, value } payload.
        // They are distinct from worker.reported so the dashboard can
        // render the condition surface as its own column.
        if (workerEvent.Condition is { } condition)
        {
            var conditionJson = JsonSerializer.Serialize(
                new ConditionJournalPayload(
                    WorkItemId: condition.WorkItemId,
                    Name: condition.Name,
                    Value: condition.Value),
                JsonSerializerOptions.Web);

            return new RunEventEntry(
                Guid.NewGuid(),
                owner,
                RunEventTypes.WorkerCondition,
                conditionJson,
                occurredAt);
        }

        // Drain events (harden-pi-worker-sandbox 5.2, spec D7) map to a
        // worker.drained journal entry with the artifact list. The
        // packager reads this to skip already-bundled prefixes; the
        // worker side never blocks complete/fail on the drain.
        if (workerEvent.Drain is { } drain)
        {
            var drainJson = JsonSerializer.Serialize(
                new DrainJournalPayload(
                    WorkItemId: drain.WorkItemId,
                    Artifacts: drain.Artifacts),
                JsonSerializerOptions.Web);

            return new RunEventEntry(
                Guid.NewGuid(),
                owner,
                RunEventTypes.WorkerDrained,
                drainJson,
                occurredAt);
        }

        // Stall-warning events (harden-worker-runtime Phase 1, design
        // D1) — the WorkerProgressWatchdog's tier 1 fires this on the
        // first tick past WorkerProgressTimeout. The host journals
        // worker.stall_warn with the last-event-age so the operator
        // can correlate with the surrounding timeline.
        if (workerEvent.StallWarn is { } stallWarn)
        {
            var stallWarnJson = JsonSerializer.Serialize(
                new StallWarnJournalPayload(
                    WorkItemId: stallWarn.WorkItemId,
                    LastEventAgeMs: stallWarn.LastEventAgeMs,
                    Tier: stallWarn.Tier),
                JsonSerializerOptions.Web);

            return new RunEventEntry(
                Guid.NewGuid(),
                owner,
                RunEventTypes.WorkerStallWarn,
                stallWarnJson,
                occurredAt);
        }

        // Stall-detected events (harden-worker-runtime Phase 1, design
        // D1 + D2) — the watchdog's tier 3 fires this on the
        // fail-item call. The host journals worker.stall_detected
        // with the same four numbers the worker used to call
        // api.FailAsync so the dashboard can correlate
        // progress-stall against wall-clock breaches.
        if (workerEvent.StallDetected is { } stallDetected)
        {
            var stallDetectedJson = JsonSerializer.Serialize(
                new StallDetectedJournalPayload(
                    WorkItemId: stallDetected.WorkItemId,
                    LastEventAgeMs: stallDetected.LastEventAgeMs,
                    TurnElapsedMs: stallDetected.TurnElapsedMs,
                    RunElapsedMs: stallDetected.RunElapsedMs,
                    Tier: stallDetected.Tier,
                    Reason: stallDetected.Reason),
                JsonSerializerOptions.Web);

            return new RunEventEntry(
                Guid.NewGuid(),
                owner,
                RunEventTypes.WorkerStallDetected,
                stallDetectedJson,
                occurredAt);
        }

        // Backpressure drop events (harden-worker-runtime Phase 3,
        // design D4) — the harness events channel dropped a
        // progress-fragment because the consumer fell behind. The
        // host journals worker.events_dropped with the drop kind so
        // the operator can see sustained backpressure on a noisy
        // harness; the events_dropped_total counter increments
        // alongside (the counter is wired in Phase 2 — telemetry —
        // and lives in the worker's own Meter).
        if (workerEvent.EventsDropped is { } eventsDropped)
        {
            var eventsDroppedJson = JsonSerializer.Serialize(
                new EventsDroppedJournalPayload(
                    WorkItemId: eventsDropped.WorkItemId,
                    Kind: eventsDropped.Kind),
                JsonSerializerOptions.Web);

            return new RunEventEntry(
                Guid.NewGuid(),
                owner,
                RunEventTypes.WorkerEventsDropped,
                eventsDroppedJson,
                occurredAt);
        }

        var legacyPayload = workerEvent switch
        {
            { Start: { } start } => JsonSerializer.Serialize(start, JsonSerializerOptions.Web),
            { Activity: { } activity } => JsonSerializer.Serialize(activity, JsonSerializerOptions.Web),
            { Report: { } report } => JsonSerializer.Serialize(report, JsonSerializerOptions.Web),
            _ => null,
        };

        return legacyPayload is null
            ? null
            : new RunEventEntry(Guid.NewGuid(), owner, JournalType, legacyPayload, occurredAt);
    }

    /// <summary>
    /// Shape of the <c>verify.completed</c> / <c>verify.failed</c>
    /// jsonb payload. <see cref="ExitCode"/> is null on success and on
    /// launch failures (a runner that could not even start the process);
    /// <see cref="FailedOpcode"/> is empty on success.
    /// </summary>
    private sealed record VerifyJournalPayload(
        string WorkItemId,
        IReadOnlyList<string> Opcodes,
        string FailedOpcode,
        long? ExitCode,
        string Reason);

    /// <summary>
    /// Shape of the <c>worker.condition</c> jsonb payload
    /// (harden-pi-worker-sandbox 5.1, spec D6). Name is one of the
    /// documented sandbox conditions; Value is the boolean state.
    /// </summary>
    private sealed record ConditionJournalPayload(
        string WorkItemId,
        string Name,
        bool Value);

    /// <summary>
    /// Shape of the <c>worker.drained</c> jsonb payload
    /// (harden-pi-worker-sandbox 5.2, spec D7). Artifacts is the
    /// pre-complete flush list; the packager skips any prefix it has
    /// already bundled.
    /// </summary>
    private sealed record DrainJournalPayload(
        string WorkItemId,
        IReadOnlyList<string> Artifacts);

    /// <summary>
    /// Shape of the <c>worker.stall_warn</c> jsonb payload
    /// (harden-worker-runtime Phase 1, design D1). <c>LastEventAgeMs</c>
    /// is the input that tripped the watchdog; <c>Tier</c> is the
    /// escalation level (1 = warn, 2 = gentle-kill, 3 = fail-item) so
    /// the dashboard can render the tier in a separate column.
    /// </summary>
    private sealed record StallWarnJournalPayload(
        string WorkItemId,
        long LastEventAgeMs,
        int Tier);

    /// <summary>
    /// Shape of the <c>worker.stall_detected</c> jsonb payload
    /// (harden-worker-runtime Phase 1, design D1 + D2). The four
    /// numbers are the same the worker used to call
    /// <c>api.FailAsync</c>, so the journal entry is the operator's
    /// audit trail. <c>Reason</c> is the typed reason string the
    /// watchdog attached (e.g. <c>"worker.stall_detected"</c>,
    /// <c>"worker.turn_budget_exceeded"</c>,
    /// <c>"worker.run_budget_exceeded"</c>).
    /// </summary>
    private sealed record StallDetectedJournalPayload(
        string WorkItemId,
        long LastEventAgeMs,
        long TurnElapsedMs,
        long RunElapsedMs,
        int Tier,
        string Reason);

    /// <summary>
    /// Shape of the <c>worker.events_dropped</c> jsonb payload
    /// (harden-worker-runtime Phase 3, design D4). <c>Kind</c> is the
    /// open-set drop reason — <c>"progress"</c> today (text-delta
    /// dropped on the drop-oldest policy). Mandatory events
    /// (<c>StageStart</c>, <c>StageReport</c>, <c>agent_end</c>) never
    /// drop; they wait for the consumer.
    /// </summary>
    private sealed record EventsDroppedJournalPayload(
        string WorkItemId,
        string Kind);
}
