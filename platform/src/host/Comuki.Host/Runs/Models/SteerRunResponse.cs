namespace Comuki.Host.Runs.Models;

/// <summary>
/// Response of <c>POST /api/v1/runs/{runId}/steer</c>. On the Phase 1a
/// (no-LiveSession) runtime <see cref="Delivered"/> is always
/// <c>true</c> and <see cref="FollowUpWorkItemId"/> carries the id of
/// the queued follow-up WorkItem the handler staged. On a future
/// LiveSession runtime (Phase 1c) <see cref="Delivered"/> reports
/// whether the bidi channel accepted the typed turn and
/// <see cref="FollowUpWorkItemId"/> is omitted (the harness, not a
/// new work item, carries the authoritative turn).
/// </summary>
/// <param name="Delivered">
/// <c>true</c> when the runtime delivered the steer to the live execution
/// (Phase 1c) or successfully staged the follow-up (Phase 1a).
/// <c>false</c> when the runtime had no live stream to deliver to and no
/// follow-up is the right call — the caller may retry once the run picks
/// up a fresh worker.
/// </param>
/// <param name="FollowUpWorkItemId">
/// The id of the queued follow-up WorkItem. Always set on the Phase 1a
/// runtime; absent on the Phase 1c runtime (where the steer rides the
/// live session).
/// </param>
public sealed record SteerRunResponse(bool Delivered, Guid? FollowUpWorkItemId);
