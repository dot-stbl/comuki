using System.Runtime.CompilerServices;
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
///   <item><b>Mandatory events</b> (<c>agent_end</c>) — never drop:
///   the writer awaits a free slot. These are the load-bearing
///   items the pump needs to see for a correct run outcome;
///   their order in the stream is also preserved (an
///   <c>agent_end</c> after a flood of text-deltas is queued
///   behind the text-deltas the consumer hasn't drained yet,
///   but is never replaced).</item>
/// </list>
/// The drop-oldest branch and the consumer's drain share a single
/// <see cref="Lock"/> (<c>dropGate</c>). Without it, the writer's
/// <c>TryPeek</c> + <c>TryRead</c> pair is not atomic with the
/// reader's <c>TryRead</c>: the reader can drain the peeked
/// progress item between the two calls, the writer's <c>TryRead</c>
/// then reads a DIFFERENT (possibly mandatory) item that just
/// became the head — silently dropping a mandatory <c>agent_end</c>.
/// With the lock, the writer's peek + read pair and the reader's
/// <c>TryRead</c> are mutually exclusive: only one party can
/// observe / mutate the channel head at a time. The underlying
/// <c>Channel</c> is configured with <c>SingleReader = true</c> and
/// <c>SingleWriter = true</c>, so the lock's only job is to
/// serialise the peek-read pair with the drain — no writer-vs-writer
/// or reader-vs-reader race is possible.
/// </summary>
public sealed class WorkerEventsChannel
{
    private readonly Channel<PiEvent> channel;
    private readonly Lock dropGate = new();
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

    /// <summary>
    /// The reader the pump iterates. The custom async iterator
    /// (instead of <c>Channel&lt;PiEvent&gt;.Reader.ReadAllAsync</c>)
    /// takes <see cref="dropGate"/> around every <c>TryRead</c> so
    /// the drop and the drain observe a consistent channel head —
    /// the same head the writer's <see cref="TryDropOldestProgress"/>
    /// peeked, never a head the reader had already drained.
    /// </summary>
    /// <param name="cancellationToken">Forwarded to the channel's
    /// <c>WaitToReadAsync</c>; the iterator itself is a
    /// [EnumeratorCancellation]-attributed parameter so the
    /// compiler injects the caller's token at iteration time.</param>
    public async IAsyncEnumerable<PiEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Drain all currently-available items under the same
            // lock the writer's TryDropOldestProgress uses, so the
            // two never observe different channel heads.
            while (true)
            {
                PiEvent? item = null;
                var read = false;
                lock (dropGate)
                {
                    if (reader.TryRead(out var got))
                    {
                        item = got;
                        read = true;
                    }
                }

                if (!read)
                {
                    break;
                }

                yield return item!;
            }
        }
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
        // agent_end, which doesn't happen in practice), wait for
        // the consumer instead of dropping the mandatory item.
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
        // Lock around the peek + read pair. The reader's drain in
        // ReadAllAsync also takes this lock for every TryRead, so
        // the two operations are mutually exclusive: the reader
        // cannot drain the peeked item between TryPeek and TryRead,
        // and the writer cannot read a different head than the one
        // it peeked. This is the contract that "mandatory events
        // are never dropped" relies on.
        lock (dropGate)
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
                return false;
            }
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
}
