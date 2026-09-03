namespace Comuki.Modules.Artifacts.Domain;

/// <summary>
/// The trigger semantics for artifact packaging. A run is terminal when it
/// reaches any status that locks further work — the journal emits the
/// terminal transition with one of the strings below, and the packager
/// fires. In-progress runs are skipped; re-tries after a transient error
/// are guarded by the <see cref="RunArtifactBundle.UploadedAt"/> row.
/// </summary>
public static class ArtifactPackageTriggers
{
    /// <summary>Status wire string for a run that finished all work items successfully.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Status wire string for a run that ended in unrecoverable failure.</summary>
    public const string Failed = "failed";

    /// <summary>Status wire string for a run that was cancelled (budget, operator, escalation).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Status wire string for a run that was escalated to a human.</summary>
    public const string Escalated = "escalated";

    /// <summary>True when the status is one of the four wire strings the packager treats as terminal.</summary>
    /// <param name="status">Lower-case status wire string.</param>
    public static bool IsTerminal(string? status)
    {
        return status is Succeeded or Failed or Cancelled or Escalated;
    }
}

