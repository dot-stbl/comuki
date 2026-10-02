namespace Comuki.Host.Translator.Execution.Clone;

/// <summary>
/// Result of one source-clone child process: the exit code (null when
/// the process never started), the launch failure detail, and the
/// captured stderr (git writes its human-readable errors there — the
/// clone failure reason forwarded to <c>POST /workers/{id}/fail</c>
/// quotes it).
/// </summary>
/// <param name="ExitCode">Process exit code; <c>null</c> when the launch itself failed.</param>
/// <param name="LaunchFailureDetail">Why the process could not be started; <c>null</c> when it ran.</param>
/// <param name="StandardError">Captured stderr, truncated to a single line by the caller if used as a reason.</param>
public sealed record SourceCloneStepResult(int? ExitCode, string? LaunchFailureDetail, string StandardError);
