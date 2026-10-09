using Comuki.Host.Translator.Parsing;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Pure reader for the harness's <c>stdout</c>: line-by-line drain
/// into the events channel. Lives in its own file (PiReader.cs) so
/// the unit-test project (Comuki.Host.Translator.Unit.Runtime,
/// friend via <c>InternalsVisibleTo</c>) can target the drain loop
/// directly — the three-state branch in <see cref="ReadEventsAsync"/>
/// is the seam the over-cap-doesn't-kill-stream contract lives at.
/// </summary>
internal static class PiReader
{
    /// <summary>
    /// Drains <paramref name="stdout"/> into <paramref name="events"/>
    /// until the stream closes or cancellation trips. Each line is
    /// parsed by <see cref="StreamJsonParser.ParseLine"/> (the same
    /// parser the v1.x one-shot path uses); the channel's
    /// <c>Complete()</c> in the <c>finally</c> below flushes the
    /// consumer on shutdown.
    /// </summary>
    /// <param name="stdout">The harness's stdout.</param>
    /// <param name="events">The bounded events channel (harden-worker-runtime Phase 3, design D4).</param>
    /// <param name="maxLineLengthBytes">Per-line cap. Lines longer than this are dropped before parse (one bad line cannot OOM the process). <c>0</c> disables the cap.</param>
    /// <param name="onLineDropped">Invoked once per dropped line (line longer than <paramref name="maxLineLengthBytes"/>). Best-effort: a throw is swallowed and never propagated to the read loop.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task ReadEventsAsync(
        StreamReader stdout,
        WorkerEventsChannel events,
        int maxLineLengthBytes,
        Action? onLineDropped,
        CancellationToken cancellationToken)
    {
        var reader = new PiLineReader(stdout, onLineDropped);
        try
        {
            while (await reader.ReadLineWithCapAsync(maxLineLengthBytes, cancellationToken) is { } result)
            {
                // Explicit three-state branch: a `Dropped` line must
                // not look like EOF (the previous `is { Line: { } line }`
                // shape exited the loop on the first over-cap line —
                // a single over-cap line killed the stream, half the
                // events still buffered). `IsEof` is the only
                // terminator; `IsDropped` is the explicit "skip and
                // read the next line" path.
                if (result.IsEof)
                {
                    break;
                }

                if (result.IsDropped)
                {
                    continue;
                }

                if (result.Line is { } line)
                {
                    foreach (var piEvent in StreamJsonParser.ParseLine(line))
                    {
                        await events.WriteAsync(piEvent, cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on session dispose — the channel's Complete() call below
            // flushes the consumer
        }
        finally
        {
            events.Complete();
        }
    }
}
