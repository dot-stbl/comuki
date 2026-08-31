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
/// payload mirrors the stage record. Events before a Start (or with an
/// unparsable binding) are dropped with a warning — the protocol guarantees
/// Start first.
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
    /// <summary>Journal type of every worker-reported event.</summary>
    public const string JournalType = RunEventTypes.WorkerReported;

    public static RunEventEntry? ToEntry(RunId owner, WorkerEvent workerEvent, DateTimeOffset occurredAt)
    {
        var payload = workerEvent switch
        {
            { Start: { } start } => JsonSerializer.Serialize(start, JsonSerializerOptions.Web),
            { Activity: { } activity } => JsonSerializer.Serialize(activity, JsonSerializerOptions.Web),
            { Report: { } report } => JsonSerializer.Serialize(report, JsonSerializerOptions.Web),
            _ => null,
        };

        return payload is null
            ? null
            : new RunEventEntry(Guid.NewGuid(), owner, JournalType, payload, occurredAt);
    }
}
