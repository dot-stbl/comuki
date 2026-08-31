namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Spawns a process (production: <c>pi</c>; tests: <c>TestFakePi</c>) with
/// the stream-json output mode and yields its stdout line-by-line as it
/// arrives. This is the seam between the .NET translator and the headless
/// agent runtime.
/// </summary>
public interface IPiRunner
{
    /// <summary>
    /// Runs the configured executable on the given brief, yielding each
    /// stdout line as it arrives. Cancellation kills the process tree and
    /// ends the stream. A non-zero exit surfaces as
    /// <see cref="InvalidOperationException"/> after the last line.
    /// </summary>
    /// <param name="brief"></param>
    /// <param name="cancellationToken"></param>
    public IAsyncEnumerable<string> RunAsync(string brief, CancellationToken cancellationToken = default);
}
