using Comuki.Modules.Procedures.Application.Materialization.Dispatch;

namespace Comuki.Modules.Procedures.Application.Materialization;

/// <summary>
/// The full materialization plan: every node from the compiled graph,
/// dispatched to its owner surface and ordered by topological depth.
/// The host reads this and creates the actual work items, decisions,
/// and operations — the plan is data, not execution.
/// </summary>
/// <param name="VersionId">The pinned version this plan materializes.</param>
/// <param name="Dispatches">All node dispatches, ordered by depth then id.</param>
public sealed record MaterializationPlan(
    string VersionId,
    IReadOnlyList<NodeDispatch> Dispatches);
