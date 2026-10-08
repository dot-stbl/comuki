using System.Text;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Behaviour of <see cref="PiReader.ReadEventsAsync"/> on the
/// harness's stdout side: the drain loop's three-state branch
/// (Eof break, Dropped continue, Ok parse) is the seam where a
/// single over-cap line used to kill the stream. The fix
/// (explicit IsEof / IsDropped) lives in the loop; these tests
/// cover it through the file-static helper (the unit-test project
/// is a friend of <c>Comuki.Host.Translator</c> via
/// <c>InternalsVisibleTo</c>).
/// <para>
/// The reader writes parsed <see cref="PiEvent"/>s into a
/// <see cref="WorkerEventsChannel"/>; the test drains the channel
/// after the reader completes (the channel is closed in
/// <c>ReadEventsAsync</c>'s <c>finally</c>), and asserts the
/// expected mix of <c>Ok</c> lines and dropped (uncounted) lines.
/// </para>
/// </summary>
public sealed class PiHarnessShould
{
    [Fact(DisplayName = "Given a stream with one over-cap line then a valid line then EOF, when ReadEventsAsync drains, then the over-cap line is dropped and the valid line is parsed (stream continues past the drop)")]
    public async Task OverCapLineIsDroppedAndLoopContinuesAsync()
    {
        // The BLOCKER fix: the previous `is { Line: { } line }` shape
        // exited the loop on the first over-cap line, killing the
        // stream. The new loop treats Dropped as "skip and read the
        // next line"; this test pins the contract.
        const int cap = 24;
        // First line: well over the cap → Dropped.
        // Second line: short valid JSON-RPC event (22 chars < cap) → parsed.
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "this-line-is-way-over-the-24-char-cap\n" +
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           "{\"type\":\"agent_start\"}\n"));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var channel = new WorkerEventsChannel(capacity: 16);

        await PiReader.ReadEventsAsync(
            stdout,
            channel,
            maxLineLengthBytes: cap,
            onLineDropped: () => Interlocked.Increment(ref dropCount),
            TestContext.Current.CancellationToken);

        var events = await ToListAsync(channel);
        events.ShouldHaveSingleItem();
        events[0].ShouldBeOfType<PiEvent.AgentStartEvent>();
        dropCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a stream of [over-cap, valid, over-cap, valid, EOF], when ReadEventsAsync drains, then both over-cap lines are dropped, both valid lines are parsed, and the stream reaches EOF cleanly")]
    public async Task MultipleOverCapLinesInterleavedWithValidLinesAsync()
    {
        // Multiple over-cap lines interleaved with valid lines; the
        // loop must keep reading after each drop, and the channel
        // must close (Eof break) on the last valid line's terminator.
        const int cap = 24;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "this-line-is-way-over-the-cap\n" +       // > cap → Dropped
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           "{\"type\":\"agent_start\"}\n" +          // valid (22 chars < cap=24)
            "another-over-cap-line-here\n" +         // > cap → Dropped
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           /*lang=json,strict*/
                                                                           "{\"type\":\"agent_end\"}\n"));           // valid (20 chars < cap=24)
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var channel = new WorkerEventsChannel(capacity: 16);

        await PiReader.ReadEventsAsync(
            stdout,
            channel,
            maxLineLengthBytes: cap,
            onLineDropped: () => Interlocked.Increment(ref dropCount),
            TestContext.Current.CancellationToken);

        var events = await ToListAsync(channel);
        events.Count.ShouldBe(2);
        events[0].ShouldBeOfType<PiEvent.AgentStartEvent>();
        events[1].ShouldBeOfType<PiEvent.AgentEndEvent>();
        dropCount.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a stream that is one over-cap line with no trailing newline then EOF, when ReadEventsAsync drains, then the drop is reported, the next call closes the channel, and the channel contains no events")]
    public async Task OverCapLineThenEofClosesChannelAsync()
    {
        // The over-cap line ends at EOF rather than a newline. The
        // reader reports Dropped once; the loop iterates one more
        // time, sees Eof, breaks, and Complete()s the channel. The
        // consumer (test) reads the empty channel and gets no
        // events. The contract under test: a single over-cap line
        // without a terminator does not hang the reader.
        const int cap = 4;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("this-line-is-too-long"));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var channel = new WorkerEventsChannel(capacity: 16);

        await PiReader.ReadEventsAsync(
            stdout,
            channel,
            maxLineLengthBytes: cap,
            onLineDropped: () => Interlocked.Increment(ref dropCount),
            TestContext.Current.CancellationToken);

        var events = await ToListAsync(channel);
        events.ShouldBeEmpty();
        // Drop fired exactly once; Eof must not double-fire the
        // callback (mirrors PiLineReaderShould.OverCapLineThenEof
        // ReportsEofNotDropAsync).
        dropCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Given an empty stream (EOF on the first read), when ReadEventsAsync drains, then the channel completes with no events and no drop callback fires")]
    public async Task EmptyStreamCompletesChannelWithNoEventsAsync()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var channel = new WorkerEventsChannel(capacity: 16);

        await PiReader.ReadEventsAsync(
            stdout,
            channel,
            maxLineLengthBytes: 1024,
            onLineDropped: () => Interlocked.Increment(ref dropCount),
            TestContext.Current.CancellationToken);

        var events = await ToListAsync(channel);
        events.ShouldBeEmpty();
        dropCount.ShouldBe(0);
    }

    private static async Task<List<PiEvent>> ToListAsync(WorkerEventsChannel channel)
    {
        var list = new List<PiEvent>();
        await foreach (var piEvent in channel.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            list.Add(piEvent);
        }
        return list;
    }
}
