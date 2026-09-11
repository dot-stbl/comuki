namespace Comuki.Shared.Bootstrap.Correlation;

/// <summary>
/// Ambient correlation id of the current asynchronous flow (issue #56 §5).
/// The host's request middleware installs one id per request; the console
/// formatters read it to stamp <c>rid=…</c> onto every log line. Kept
/// separate from the subject scope on purpose — correlation is orthogonal
/// to authorization projection.
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>The ambient correlation id, or null when no flow established one.</summary>
    public string? CurrentId { get; }

    /// <summary>Installs the id for the current flow and returns the restore handle.</summary>
    /// <param name="requestId">The correlation id to install.</param>
    public IDisposable Begin(string requestId);
}
