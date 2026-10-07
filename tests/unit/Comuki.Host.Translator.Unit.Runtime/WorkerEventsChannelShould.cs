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
}
