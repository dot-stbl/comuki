namespace Comuki.Host.Translator.Execution.Clone;

/// <summary>
/// Process seam for the source clone (harden-pi-worker-sandbox 4.3).
/// Unlike <c>IRestoreProcessRunner</c>, the clone needs per-process
/// environment overrides (the throwaway
/// <c>GIT_CONFIG_GLOBAL</c> credential file) and captured stderr — the
/// restore runner supports neither, and widening its contract for one
/// caller would break its fakes. Tests substitute a fake to assert the
/// argv, the environment, and the config-file lifecycle.
/// </summary>
public interface ISourceCloneProcessRunner
{
    /// <summary>Runs one child process to completion, no shell, stdout discarded, stderr captured.</summary>
    /// <param name="executable">Executable path (<c>git</c> in production).</param>
    /// <param name="arguments">Argument list — one entry per argument, no shell quoting.</param>
    /// <param name="environment">Extra environment entries for this process only; the container env stays untouched.</param>
    /// <param name="workingDirectory">Working directory for the child; <c>null</c> inherits the Translator's.</param>
    /// <param name="cancellationToken">Cancels the wait; the child is killed best-effort.</param>
    /// <exception cref="OperationCanceledException">the token fired before the process exited.</exception>
    public Task<SourceCloneStepResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}
