namespace Comuki.Host.Runs.Models;

/// <summary>
/// Body of <c>POST /api/v1/runs/{runId}/cancel</c>. The <c>reason</c>
/// (when present) is journalled verbatim on the <c>run.status_changed</c>
/// event's jsonb payload — the operator's note that survives the run's
/// timeline.
/// </summary>
public sealed class CancelRunRequest
{
    /// <summary>Optional human-readable reason; empty / null → no reason field in the journal payload.</summary>
    public string? Reason { get; init; }
}
