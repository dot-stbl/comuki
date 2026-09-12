namespace Comuki.Shared.Contracts.Runs;

/// <summary>
/// Stable wire keys for the engine's <c>RunStatus</c> — the exact
/// PascalCase text EF's <c>HasConversion&lt;string&gt;()</c> persists (the
/// queue's raw-SQL and partial-index predicates pin the same text via
/// <c>nameof</c>). The enum itself lives in
/// <c>Comuki.Engine.Orchestration.Domain</c>; these strings are the only
/// representation modules that must not reference the engine (Intake's
/// tracker sync-back bridge) are allowed to depend on. A rename on either
/// side is caught by the drift guard in <c>Comuki.Architecture.Tests</c>,
/// which pins every constant here against <c>nameof(RunStatus.*)</c>.
/// </summary>
public static class RunStatuses
{
    /// <summary>Admitted, waiting to be claimed by a worker.</summary>
    public const string Queued = "Queued";

    /// <summary>Blocked on an external event (approval, escalation reply, …).</summary>
    public const string Waiting = "Waiting";

    /// <summary>Actively executing.</summary>
    public const string Running = "Running";

    /// <summary>Finished successfully.</summary>
    public const string Succeeded = "Succeeded";

    /// <summary>Finished with an error.</summary>
    public const string Failed = "Failed";

    /// <summary>Abandoned.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>Escalated to a human operator.</summary>
    public const string Escalated = "Escalated";
}
