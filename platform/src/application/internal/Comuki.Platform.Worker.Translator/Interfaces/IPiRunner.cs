namespace Comuki.Platform.Worker.Translator.Interfaces;

/// <summary>
/// Spawns a process (by default <c>pi</c>, but the executable is injected so tests
/// can substitute a fixture-emitter like <c>TestFakePi</c>) and yields its
/// stdout line-by-line as it produces them.
///
/// This is the seam between the .NET app and the headless agent runtime —
/// everything in <see cref="ITranslator"/> and <c>StreamJsonParser</c> sits on top
/// of this <see cref="IAsyncEnumerable{T}"/> of raw stdout lines.
/// </summary>
public interface IPiRunner
{
    /// <summary>
    /// Runs the configured executable with the given prompt and stream-json output
    /// format, yielding each stdout line as it arrives. Cancellation kills the
    /// process and disposes the stream. The process's exit code and stderr are
    /// surfaced as exceptions (non-zero exit) or logged (stderr).
    /// </summary>
    public IAsyncEnumerable<string> RunAsync(string prompt, CancellationToken cancellationToken = default);
}
