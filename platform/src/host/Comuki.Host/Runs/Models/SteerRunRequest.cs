namespace Comuki.Host.Runs.Models;

/// <summary>
/// Body of <c>POST /api/v1/runs/{runId}/steer</c> (add-orchestra §1 — Baton).
/// Carries the operator's steer text — the same text the live
/// <c>WorkerCommandHandler</c> appends to the worker's
/// <c>comuki-injected-context.md</c>, and the same text the Phase 1a
/// follow-up WorkItem's <c>brief</c> reproduces. The follow-up is the
/// no-LiveSession runtime's only way to surface the steer to a fresh
/// worker after the reaper reclaims a dead lease.
/// </summary>
public sealed class SteerRunRequest
{
    /// <summary>The operator's steer text. Required; non-empty after trim.</summary>
    public string Text { get; init; } = string.Empty;
}
