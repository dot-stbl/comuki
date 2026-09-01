namespace Comuki.Shared.Contracts.Runs;

/// <summary>
/// Read-model row of one run for listing surfaces (chat tools, dashboards).
/// Status is the wire string of the engine's <c>RunStatus</c> (lower-cased).
/// </summary>
/// <param name="Id">Run id.</param>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="Status">Run status wire string (queued, running, …).</param>
/// <param name="CreatedAt">When the run was admitted.</param>
/// <param name="UpdatedAt">Last status change.</param>
public sealed record RunSummary(Guid Id, Guid ProjectId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
