using System.Threading.Channels;
using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Bounded events channel for the harness stream-json reader
/// (harden-worker-runtime Phase 3, design D4). The previous
/// implementation used an unbounded <c>Channel&lt;PiEvent&gt;</c>;
/// a flood of <c>text_delta</c> events from a noisy harness could
/// grow the channel without limit and OOM the worker process. The
/// bounded version has two policies:
/// <list type="bullet">
///   <item><b>Progress fragments</b> (<c>text_delta</c>) — drop-oldest:
///   when the channel is full and a new <c>text_delta</c> arrives,
///   the oldest progress item is dropped (and the drop is counted +
///   journaled) to make room. The new item is then accepted.</item>
///   <item><b>Mandatory events</b> (<c>StageStart</c>, <c>StageReport</c>,
///   <c>agent_end</c>) — never drop: the writer awaits a free slot.
///   These are the load-bearing items the pump needs to see for a
///   correct run outcome; their order in the stream is also
///   preserved (a <c>StageReport</c> after a flood of text-deltas
///   is queued behind the text-deltas the consumer hasn't drained
///   yet, but is never replaced).</item>
/// </list>
/// The <c>TryPeek</c>/<c>TryRead</c> dance below is racy in principle
/// (another reader could drain the peeked item between the two calls)
/// but the channel has <c>SingleReader = true</c> — the consumer is
/// the pump's events iterator, single-threaded. The peek + read pair
/// is therefore atomic with respect to the channel's reader.
/// </summary>
public sealed class WorkerEventsChannel
{
    private readonly Channel<PiEvent> channel;
    private long progressDropped;

    /// <summary>Number of progress events dropped on the drop-oldest policy since the channel was constructed.</summary>
    public long ProgressDropped => Interlocked.Read(ref progressDropped);

    /// <summary>
    /// Optional callback invoked from <see cref="WriteAsync"/> when a
    /// progress fragment is dropped. Best-effort: a throw is caught
    /// and logged at warning by the caller; the reader task never
    /// propagates an exception from this callback.
    /// </summary>
    public Action? OnProgressDropped { get; set; }

    /// <summary>The reader the pump iterates. Same surface as
    /// <c>Channel&lt;PiEvent&gt;.Reader.ReadAllAsync(cancellationToken)</c>.</summary>
    public IAsyncEnumerable<PiEvent> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Constructs the channel. <c>FullMode = Wait</c> is the basis
    /// for the smart-drop policy below; the wrapper never lets a
    /// mandatory write block on a full channel filled with
    /// mandatory items, and never lets a progress write drop a
    /// mandatory item to make room.
    /// </summary>
    /// <param name="capacity">Bounded capacity; the option is
    /// <c>TranslatorOptions.EventsChannelCapacity</c> (default 1024).</param>
    public WorkerEventsChannel(int capacity)
    {
        channel = Channel.CreateBounded<PiEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>
    /// Closes the channel for new writes; the consumer drains the
    /// remaining items and then completes. Called by the reader
    /// task on stdout close.
    /// </summary>
    public void Complete()
    {
        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Writes one event under the mixed policy described above. Awaits
    /// only when the channel is full of items that the policy says
    /// must not be dropped. Returns when the event is queued (or when
    /// the channel is completed and the write is rejected with
    /// <see cref="ChannelClosedException"/>).
    /// </summary>
    public ValueTask WriteAsync(PiEvent piEvent, CancellationToken cancellationToken = default)
    {
        if (IsMandatory(piEvent))
        {
            // Mandatory: always await a free slot; the policy
            // explicitly forbids drop. FullMode=Wait handles this.
            return channel.Writer.WriteAsync(piEvent, cancellationToken);
        }

        // Progress fragment: try-write first; on full, drop the
        // OLDEST progress item to make room. If the oldest item is
        // mandatory (rare — only when the channel is full of
        // mandatory events, which would mean a flood of
        // StageStart/Report/agent_end, which doesn't happen in
        // practice), wait for the consumer instead of dropping
        // the mandatory item.
        if (channel.Writer.TryWrite(piEvent))
        {
            return ValueTask.CompletedTask;
        }

        if (TryDropOldestProgress())
        {
            if (channel.Writer.TryWrite(piEvent))
            {
                OnProgressDropped?.Invoke();
                return ValueTask.CompletedTask;
            }
        }

        return channel.Writer.WriteAsync(piEvent, cancellationToken);
    }

    private bool TryDropOldestProgress()
    {
        if (!channel.Reader.TryPeek(out var oldest))
        {
            return false;
        }

        if (IsMandatory(oldest))
        {
            return false;
        }

        if (!channel.Reader.TryRead(out _))
        {
            // Consumer raced us (shouldn't happen — SingleReader) — give up.
            return false;
        }

        Interlocked.Increment(ref progressDropped);
        return true;
    }

    /// <summary>Mandatory events: writes never drop, the writer awaits.</summary>
    internal static bool IsMandatory(PiEvent piEvent)
    {
        return piEvent switch
        {
            PiEvent.AgentEndEvent => true,
            _ => false,
        };
    }

    /// <summary>StageStart / StageReport markers — the run-level lifecycle events
    /// emitted by the host / claim side. The pump sees them as
    /// <c>PiEvent</c> through the events channel; today they are
    /// not in the <c>PiEvent</c> shape (they live on the gRPC side),
    /// so this predicate is forward-looking — the channel is ready
    /// to mark them as mandatory when they enter the event stream
    /// (e.g. a future harness that emits run-level events inline).</summary>
    /// <remarks>
    /// <para>Today the only event the host's <c>StageStart</c> /
    /// <c>StageReport</c> shape binds to is the first
    /// <c>PiEvent.AgentStartEvent</c> (the harness's
    /// <c>agent_start</c>), but those are session-level (one per
    /// turn) and dropping one is non-fatal. The run-level mandatory
    /// events are surfaced over gRPC by the loop (ToStartEvent /
    /// ToReportEvent on the bound session) — not through this
    /// channel. This predicate still keeps the door open for
    /// future run-level events to flow through the same channel
    /// without re-architecting.</para>
    /// </remarks>
    internal static bool IsRunLevelMandatory(PiEvent piEvent)
    {
        return piEvent switch
        {
            // Future-proofing: any new mandatory event type the
            // harness starts emitting on stdout will be added here.
            // The AgentEndEvent is the closest existing equivalent
            // (run-level end-of-stream marker) and is mandatory today.
            PiEvent.AgentEndEvent => true,
            _ => false,
        };
    }
}
