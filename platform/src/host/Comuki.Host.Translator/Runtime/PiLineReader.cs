using System.Text;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Line reader for the harness's <c>stdout</c>: drains one
/// line at a time, capping each line at the configured cap
/// (passed to <see cref="ReadLineWithCapAsync"/>). A line longer
/// than the cap is dropped (the rest of the line is consumed from
/// the stream so the next call sees the start of the next line),
/// and the method returns <see cref="ReadLineResult.Dropped"/> so
/// the outer loop keeps reading — a dropped line must not look
/// like EOF, otherwise the stream would die with half the events
/// still buffered.
/// <para>
/// The reader is an instance class because it owns a per-call
/// buffer for async reads (the
/// <see cref="System.Threading.Channels.ChannelReader{T}"/> is not
/// the right surface — StreamReader has no async Peek and the
/// previous char-by-char Peek+Read blocked the calling thread).
/// One reader per <c>ReadEventsAsync</c> invocation; the harness
/// creates the reader in <see cref="PiHarness.StartSessionAsync"/>
/// and the background task is its sole consumer.
/// </para>
/// <para>
/// The drop callback is invoked once per dropped line; the caller
/// (the harness) wires it to
/// <see cref="WorkerEventsChannel.OnProgressDropped"/> semantics —
/// same journal entry (<c>worker.events_dropped</c>), no
/// special-casing for "channel-internal drop" vs "reader-side
/// drop" at the pump.
/// </para>
/// </summary>
/// <remarks>Constructs a reader over <paramref name="stdout"/> with an optional drop callback.</remarks>
/// <param name="stdout">The harness's stdout.</param>
/// <param name="onLineDropped">Invoked once per dropped line (line longer than the cap). Best-effort: a throw is caught and ignored by the caller.</param>
internal sealed class PiLineReader(StreamReader stdout, Action? onLineDropped = null)
{
    private const int ReadBufferSize = 4096;

    private readonly StreamReader stdout = stdout;
    private readonly char[] readBuffer = new char[ReadBufferSize];
    private readonly StringBuilder lineBuffer = new(ReadBufferSize);
    private readonly Action? onLineDropped = onLineDropped;
    private int position;
    private int filled;
    private bool endOfStream;

    /// <summary>
    /// Reads the next line, capping its length to
    /// <paramref name="maxLineLengthBytes"/>. Returns one of three
    /// outcomes: <see cref="ReadLineResult.Eof"/> when the stream
    /// closed, <see cref="ReadLineResult.Dropped"/> when the line
    /// was over the cap (and the rest of the line was consumed), or
    /// <see cref="ReadLineResult.Ok"/> with the line text.
    /// </summary>
    /// <param name="maxLineLengthBytes">Per-line cap. <c>0</c> disables the cap (uses the underlying <c>StreamReader.ReadLineAsync</c>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<ReadLineResult> ReadLineWithCapAsync(int maxLineLengthBytes, CancellationToken cancellationToken)
    {
        if (maxLineLengthBytes <= 0)
        {
            var line = await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            return line is null ? ReadLineResult.Eof : ReadLineResult.Ok(line);
        }

        lineBuffer.Clear();
        var dropped = false;
        while (true)
        {
            if (position >= filled)
            {
                if (endOfStream)
                {
                    if (dropped)
                    {
                        NotifyDropped();
                        return ReadLineResult.Dropped;
                    }

                    return lineBuffer.Length == 0
                        ? ReadLineResult.Eof
                        : ReadLineResult.Ok(lineBuffer.ToString());
                }

                filled = await stdout.ReadAsync(readBuffer.AsMemory(0, ReadBufferSize), cancellationToken).ConfigureAwait(false);
                position = 0;
                if (filled == 0)
                {
                    endOfStream = true;
                    if (dropped)
                    {
                        NotifyDropped();
                        return ReadLineResult.Dropped;
                    }

                    return lineBuffer.Length == 0
                        ? ReadLineResult.Eof
                        : ReadLineResult.Ok(lineBuffer.ToString());
                }
            }

            var c = readBuffer[position++];
            if (c == '\n')
            {
                if (dropped)
                {
                    NotifyDropped();
                    return ReadLineResult.Dropped;
                }

                return ReadLineResult.Ok(lineBuffer.ToString());
            }

            if (lineBuffer.Length < maxLineLengthBytes)
            {
                lineBuffer.Append(c);
            }
            else
            {
                dropped = true;
            }
        }
    }

    private void NotifyDropped()
    {
        try
        {
            onLineDropped?.Invoke();
        }
        catch (Exception)
        {
            // The pump's existing drop callback is best-effort; a
            // throw from the test fake or the journal send should
            // never reach the read loop.
        }
    }
}

/// <summary>Outcome of a single <see cref="PiLineReader.ReadLineWithCapAsync"/> call.</summary>
internal readonly record struct ReadLineResult
{
    private ReadLineResult(string? line, bool isEof, bool isDropped)
    {
        Line = line;
        IsEof = isEof;
        IsDropped = isDropped;
    }

    /// <summary>The line text; non-null when <see cref="IsOk"/>.</summary>
    public string? Line { get; }

    /// <summary>The stream reached end-of-file; the outer loop should exit.</summary>
    public bool IsEof { get; }

    /// <summary>The line was over the cap and was dropped; the outer loop should continue reading.</summary>
    public bool IsDropped { get; }

    /// <summary>The line is present in <see cref="Line"/>.</summary>
    public bool IsOk => Line is not null;

    /// <summary>End-of-file result — the stream closed.</summary>
    public static ReadLineResult Eof { get; } = new(null, isEof: true, isDropped: false);

    /// <summary>Dropped result — the line was over the cap; the stream is still alive.</summary>
    public static ReadLineResult Dropped { get; } = new(null, isEof: false, isDropped: true);

    /// <summary>Ok result carrying the line text.</summary>
    /// <param name="line">The line read from the stream, without the trailing newline.</param>
    public static ReadLineResult Ok(string line)
    {
        return new(line, isEof: false, isDropped: false);
    }
}
