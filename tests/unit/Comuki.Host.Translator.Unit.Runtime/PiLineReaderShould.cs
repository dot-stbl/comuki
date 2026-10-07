using System.Text;
using Comuki.Host.Translator.Runtime;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Line reader behaviour on the harness stdout side: lines longer
/// than the cap are dropped (and the rest of the line is consumed
/// from the stream), the next valid line is read, and the loop
/// continues. The reader is async — no synchronous Peek+Read pair
/// on the calling thread. The drop callback is invoked exactly
/// once per dropped line.
/// </summary>
public sealed class PiLineReaderShould
{
    [Fact(DisplayName = "Given a stream with a short valid line, when read, then the line is returned")]
    public async Task ShortLineIsReturnedAsync()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello\n"));
        using var stdout = new StreamReader(stream);

        var reader = new PiLineReader(stdout);

        var result = await reader.ReadLineWithCapAsync(maxLineLengthBytes: 1024, TestContext.Current.CancellationToken);

        result.IsOk.ShouldBeTrue();
        result.Line.ShouldBe("hello");
    }

    [Fact(DisplayName = "Given a stream with a line longer than the cap, when read, then Dropped is returned and the next valid line is read on the next call")]
    public async Task OverCapLineIsDroppedAndLoopContinuesAsync()
    {
        // 1.5x cap → first read = Dropped; the second read picks up
        // the next line.
        const int cap = 8;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("this-line-is-too-long\nshort\n"));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var reader = new PiLineReader(stdout, onLineDropped: () => Interlocked.Increment(ref dropCount));

        var first = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        first.IsDropped.ShouldBeTrue();
        first.Line.ShouldBeNull();
        dropCount.ShouldBe(1);

        var second = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        second.IsOk.ShouldBeTrue();
        second.Line.ShouldBe("short");
    }

    [Fact(DisplayName = "Given a stream with two over-cap lines in a row, when read, then both are dropped and the valid line after them is parsed")]
    public async Task MultipleOverCapLinesAreAllDroppedAsync()
    {
        const int cap = 4;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("line-one\nline-two\nok\n"));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var reader = new PiLineReader(stdout, onLineDropped: () => Interlocked.Increment(ref dropCount));

        var first = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        first.IsDropped.ShouldBeTrue();

        var second = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        second.IsDropped.ShouldBeTrue();

        var third = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        third.IsOk.ShouldBeTrue();
        third.Line.ShouldBe("ok");

        dropCount.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a stream with a final line that has no trailing newline, when read, then the line is returned and the next call returns Eof")]
    public async Task LastLineWithoutNewlineIsReturnedAndEofFollowsAsync()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        using var stdout = new StreamReader(stream);

        var reader = new PiLineReader(stdout);

        var first = await reader.ReadLineWithCapAsync(maxLineLengthBytes: 1024, TestContext.Current.CancellationToken);
        first.IsOk.ShouldBeTrue();
        first.Line.ShouldBe("hello");

        var second = await reader.ReadLineWithCapAsync(maxLineLengthBytes: 1024, TestContext.Current.CancellationToken);
        second.IsEof.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a stream with one over-cap line then EOF, when read, then the drop is reported and the next call returns Eof (NOT a drop)")]
    public async Task OverCapLineThenEofReportsEofNotDropAsync()
    {
        // The line ends with EOF rather than a newline. The reader
        // returns the line state on EOF (dropped or empty), but a
        // follow-up call must return Eof so the outer loop exits.
        const int cap = 4;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("this-line-is-too-long"));
        using var stdout = new StreamReader(stream);

        var dropCount = 0;
        var reader = new PiLineReader(stdout, onLineDropped: () => Interlocked.Increment(ref dropCount));

        var first = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        first.IsDropped.ShouldBeTrue();
        dropCount.ShouldBe(1);

        var second = await reader.ReadLineWithCapAsync(maxLineLengthBytes: cap, TestContext.Current.CancellationToken);
        second.IsEof.ShouldBeTrue();
        // The drop callback fired exactly once — the EOF must not
        // also report a drop.
        dropCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a cap of 0, when read, then the underlying ReadLineAsync is used (no cap)")]
    public async Task ZeroCapDisablesCapAsync()
    {
        // 1000-char line; with cap=0, it is returned verbatim.
        var line = new string('a', 1000);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(line + "\n"));
        using var stdout = new StreamReader(stream);

        var reader = new PiLineReader(stdout);

        var result = await reader.ReadLineWithCapAsync(maxLineLengthBytes: 0, TestContext.Current.CancellationToken);

        result.IsOk.ShouldBeTrue();
        result.Line!.Length.ShouldBe(1000);
    }

    [Fact(DisplayName = "Given a line that contains no characters past the cap, when read, then the line is accepted (cap is a soft cap, not a hard truncation)")]
    public async Task LineAtCapIsAcceptedAsync()
    {
        // 4 chars + newline; cap=4 → all 4 chars go into the line,
        // no drop.
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("abcd\n"));
        using var stdout = new StreamReader(stream);

        var reader = new PiLineReader(stdout);

        var result = await reader.ReadLineWithCapAsync(maxLineLengthBytes: 4, TestContext.Current.CancellationToken);

        result.IsOk.ShouldBeTrue();
        result.Line.ShouldBe("abcd");
    }
}
