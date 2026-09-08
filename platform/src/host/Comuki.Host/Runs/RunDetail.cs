namespace Comuki.Host.Runs;

/// <summary>
/// Full wire shape of one run for <c>GET /api/v1/runs/{runId}</c>. Carries
/// everything the FE <c>RunDetail</c> mapper needs without a second round-trip.
///
/// Several fields (Title, App, Model, CostUsd, Tokens, Rules) are placeholders
/// for data the platform does not yet store on the run row — see
/// <see cref="GetRunDetailHandler"/> for what is and is not populated, and
/// audit-report §2.7 for the broader metering / plan-extent gaps.
/// </summary>
/// <param name="Id">Run id (UUIDv7).</param>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="Status">Wire status string (lowercase).</param>
/// <param name="CreatedAt">Run admit timestamp.</param>
/// <param name="UpdatedAt">Last status-change timestamp.</param>
/// <param name="Title">Reserved for the brain-authored run title; empty until brain stores it.</param>
/// <param name="App">Reserved for the worker-app key; empty until brain stores it.</param>
/// <param name="Model">Reserved for the lead/worker split; <c>"worker"</c> as the safe default.</param>
/// <param name="CostUsd">Per-run token spend in USD; <c>0</c> until proxy metering writes <c>usage_events</c>.</param>
/// <param name="Tokens">Per-run token count; <c>0</c> for the same reason.</param>
/// <param name="Brief">First work-item brief (raw jsonb string); empty when the run has no work items yet.</param>
/// <param name="WorkItems">Plan nodes, with their <c>dependsOn</c> lists joined from <c>work_item_dependencies</c>.</param>
/// <param name="Events">Recent journal rows (top 20, newest first) — run-status, work-item-status, lease-reaper, etc.</param>
/// <param name="Rules">Control-plane rule names applied; empty until control-plane exposes the lookup.</param>
/// <param name="Revision">Pinned SDK + control-plane revisions (image digest + profiles git ref).</param>
public sealed record RunDetail(
    Guid Id,
    Guid ProjectId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Title,
    string App,
    string Model,
    decimal CostUsd,
    long Tokens,
    string Brief,
    IReadOnlyList<RunDetailWorkItem> WorkItems,
    IReadOnlyList<RunDetailEvent> Events,
    IReadOnlyList<string> Rules,
    RunDetailRevision Revision);

/// <summary>Wire row for one work-item in the run's plan.</summary>
/// <param name="Id">Work-item id.</param>
/// <param name="Profile">Worker profile key (e.g. <c>implement</c>, <c>explore-readonly</c>).</param>
/// <param name="Label">Brain-authored step name; empty when not provided.</param>
/// <param name="Status">Wire status string (lowercase).</param>
/// <param name="DependsOn">Ids of prerequisite work items (joined from <c>work_item_dependencies</c>).</param>
/// <param name="Cost">Reserved per-item spend in USD; <c>0</c> until usage_events is wired.</param>
/// <param name="Tokens">Reserved per-item token count; <c>0</c> for the same reason.</param>
/// <param name="StartedAt">First transition away from <c>Queued</c>, or <c>null</c> while still queued.</param>
public sealed record RunDetailWorkItem(
    Guid Id,
    string Profile,
    string Label,
    string Status,
    IReadOnlyList<Guid> DependsOn,
    decimal Cost,
    long Tokens,
    DateTimeOffset? StartedAt);

/// <summary>Wire row for one journal event in the run timeline (newest first).</summary>
/// <param name="Id">Event id.</param>
/// <param name="WorkItemId">Work-item the entry is about, when the payload carries one.</param>
/// <param name="Type">Stable dot.case event type.</param>
/// <param name="OccurredAt">When the entry happened.</param>
/// <param name="PayloadJson">Raw payload JSON, or null when omitted.</param>
public sealed record RunDetailEvent(
    Guid Id,
    Guid? WorkItemId,
    string Type,
    DateTimeOffset OccurredAt,
    string? PayloadJson);

/// <summary>Pinned revisions of the worker image and the control-plane profiles ref.</summary>
/// <param name="Rules">Git ref of the control-plane / profiles repo (pinned on the work item).</param>
/// <param name="Sdk">Worker image + digest (pinned on the work item).</param>
public sealed record RunDetailRevision(string Rules, string Sdk);
