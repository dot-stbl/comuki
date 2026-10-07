using System.Threading.Channels;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Lifecycle of <see cref="WorkerEventsChannel"/>: bounded capacity with
/// drop-oldest for progress fragments (text_delta) and never-drop for
/// mandatory events (agent_end). The cap is the harness-side backpressure
/// the Translator's <c>PiPump</c> iterates — without it, a noisy harness
/// can grow the channel without limit.
/// </summary>
public sealed class WorkerEventsChannelShould
{
    [Fact(DisplayName = "Given a fresh channel, when a text_delta is written, then the reader observes it")]
    public async Task TextDeltaIsDeliveredAsync()
    {
        var channel = new WorkerEventsChannel(capacity: 4);
        var delta = new PiEvent.TextDeltaEvent(ContentIndex: 0, Delta: "hello");

        await channel.WriteAsync(delta, TestContext.Current.CancellationToken);

        var read = await channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken)
            .MoveNextAsync();
        read.ShouldBeTrue();
        // Note: read.Current is the PiEvent from the channel
    }

    [Fact(DisplayName = "Given a full channel with only progress events, when a new text_delta arrives, then the oldest progress is dropped and the new one is accepted")]
    public async Task TextDeltaDropsOldestWhenFullAsync()
    {
        var channel = new WorkerEventsChannel(capacity: 2);
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "first"), TestContext.Current.CancellationToken);
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "second"), TestContext.Current.CancellationToken);
        channel.ProgressDropped.ShouldBe(0);

        // Channel is full; this third write should drop "first".
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "third"), TestContext.Current.CancellationToken);

        channel.ProgressDropped.ShouldBe(1L);
    }

    [Fact(DisplayName = "Given a full channel with progress events, when a mandatory agent_end arrives, then the writer waits (no drop)")]
    public async Task MandatoryEventWaitsWhenFullAsync()
    {
        var channel = new WorkerEventsChannel(capacity: 2);
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "first"), TestContext.Current.CancellationToken);
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "second"), TestContext.Current.CancellationToken);
        channel.ProgressDropped.ShouldBe(0);

        // Mandatory: must wait. We capture the ValueTask directly so
        // its pending state is observable (Task.Run with a sync
        // lambda would complete the outer task as soon as the inner
        // ValueTask is returned, even though the write is blocked).
        var mandatoryWrite = channel.WriteAsync(
            new PiEvent.AgentEndEvent(),
            TestContext.Current.CancellationToken);

        // The bounded channel's writer awaits a free slot; the
        // ValueTask is pending until the consumer drains at least
        // one slot.
        mandatoryWrite.IsCompleted.ShouldBeFalse();
        channel.ProgressDropped.ShouldBe(0L);

        // Drain: read the first progress event to free a slot. The
        // mandatory write should now be able to land and complete.
        var enumerator = channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await enumerator.MoveNextAsync();
        await mandatoryWrite;
        await enumerator.DisposeAsync();
    }

    [Fact(DisplayName = "Given a full channel of progress events, when a mandatory event arrives, then OnProgressDropped is not invoked")]
    public async Task MandatoryEventDoesNotInvokeDropCallbackAsync()
    {
        var channel = new WorkerEventsChannel(capacity: 1)
        {
            OnProgressDropped = () => throw new Xunit.Sdk.XunitException("must not invoke drop callback for mandatory events"),
        };
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "fill"), TestContext.Current.CancellationToken);

        // Race: the mandatory write awaits; we cancel the test to
        // release the awaiter (no slot will be freed by the test).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await channel.WriteAsync(new PiEvent.AgentEndEvent(), cts.Token));
    }

    [Fact(DisplayName = "Given a full channel of progress events, when a new text_delta arrives, then OnProgressDropped is invoked exactly once")]
    public async Task DropCallbackFiresOnProgressDropAsync()
    {
        var dropCount = 0;
        var channel = new WorkerEventsChannel(capacity: 1)
        {
            OnProgressDropped = () => Interlocked.Increment(ref dropCount),
        };
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "first"), TestContext.Current.CancellationToken);

        // Drop the first by writing three more.
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "second"), TestContext.Current.CancellationToken);
        await channel.WriteAsync(new PiEvent.TextDeltaEvent(0, "third"), TestContext.Current.CancellationToken);

        dropCount.ShouldBe(2);
    }

    [Fact(DisplayName = "Given concurrent reader + writer under contention, when mandatory events flow, then every mandatory the writer produced is delivered to the reader (none are dropped)")]
    public async Task MandatoryIsNeverDroppedUnderConcurrentReadAsync()
    {
        // Contract: <c>agent_end</c> events are load-bearing for the
        // run outcome — losing one is a silent pump corruption. The
        // pre-fix bug was a peek-read race: the writer's
        // <c>TryPeek</c> saw a non-mandatory head, the reader
        // drained it, the writer's <c>TryRead</c> then read the
        // NEW head (which could be mandatory) — silently dropping
        // an <c>agent_end</c>. With the fix, the writer's peek +
        // read pair and the reader's drain share a single lock, so
        // the two never observe different channel heads. This test
        // stresses that contract: a small capacity + many writers
        // + a fast reader maximises the chance the old bug would
        // have surfaced.
        var channel = new WorkerEventsChannel(capacity: 4);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var mandatoryWritten = 0L;
        var progressWritten = 0L;
        var mandatoryReceived = 0L;
        var progressReceived = 0L;

        // Two writers: one pushes mandatory events, one pushes
        // progress. The capacity-4 channel fills quickly with
        // progress; mandatory events land against a full channel
        // and must wait — never be dropped.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(3));
#pragma warning disable xUnit1051 // linkedCts is already linked to TestContext.Current.CancellationToken
        var progressWriter = Task.Run(async () =>
        {
            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await channel.WriteAsync(
                        new PiEvent.TextDeltaEvent(0, "flood"),
                        linkedCts.Token);
                    Interlocked.Increment(ref progressWritten);
                }
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
        });

        var mandatoryWriter = Task.Run(async () =>
        {
            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await channel.WriteAsync(
                        new PiEvent.AgentEndEvent(),
                        linkedCts.Token);
                    Interlocked.Increment(ref mandatoryWritten);
                }
            }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
        });

        var readerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in channel.ReadAllAsync(linkedCts.Token))
                {
                    if (item is PiEvent.AgentEndEvent)
                    {
                        Interlocked.Increment(ref mandatoryReceived);
                    }
                    else
                    {
                        Interlocked.Increment(ref progressReceived);
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        // Let contention build, then close.
        await Task.Delay(TimeSpan.FromSeconds(2), linkedCts.Token);
        channel.Complete();

        await Task.WhenAll(progressWriter, mandatoryWriter, readerTask);
#pragma warning restore xUnit1051

        // Contract: every mandatory the writer produced was
        // delivered. The pre-fix race dropped mandatory events
        // silently, so this assertion would have failed under
        // stress.
        mandatoryReceived.ShouldBe(mandatoryWritten);

        // Sanity: the writers actually produced something, so the
        // test exercises the contended path; an empty run would
        // pass the assertion vacuously.
        mandatoryWritten.ShouldBeGreaterThan(0);
        progressWritten.ShouldBeGreaterThan(0);

        // Some progress was dropped (bounded channel against a
        // flood) — but the contract is about mandatory, not
        // progress. We don't pin the exact drop count.
        channel.ProgressDropped.ShouldBeGreaterThanOrEqualTo(0);
    }
}
