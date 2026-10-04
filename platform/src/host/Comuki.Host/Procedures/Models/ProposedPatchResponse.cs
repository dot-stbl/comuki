namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for a proposed patch
/// (<c>POST /api/v1/procedures/{projectId}/{procedureKey}/propose-patch</c>).
/// The chat surface has no publication path — the human publishes from
/// Studio after reviewing the rendered diff.
/// 
/// <para>
/// Carries the full semantic diff payload: per-bucket entries (added
/// nodes, removed node ids, rewired edges, re-parameterized nodes) so
/// Studio can render "added 2, removed 1, rewired 1" inline without
/// re-running the diff computer. <see cref="Unchanged"/> is true when
/// the patch was a no-op (validator catches obvious ones; the post-
/// application view catches the rest).
/// </para>
/// </summary>
public sealed record ProposedPatchResponse(
    string PatchId,
    string BaseVersionId,
    string Rationale,
    bool Unchanged,
    string DiffSummary,
    IReadOnlyList<ProposedPatchResponse.AddedNodeDto> AddedNodes,
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<ProposedPatchResponse.RewiredEdgeDto> RewiredEdges,
    IReadOnlyList<ProposedPatchResponse.ReParameterizedNodeDto> ReParameterizedNodes)
{
    /// <summary>One node inserted by the patch.</summary>
    /// <param name="Id">Graph-local identifier.</param>
    /// <param name="KindKey">Catalog kind the node instances.</param>
    /// <param name="Parameters">Per-kind parameter overrides.</param>
    public sealed record AddedNodeDto(
        string Id,
        string KindKey,
        IReadOnlyDictionary<string, string> Parameters);

    /// <summary>Reference to an edge in the diff.</summary>
    /// <param name="FromNodeId">Source node id.</param>
    /// <param name="FromPort">Source outcome port.</param>
    /// <param name="ToNodeId">Destination node id.</param>
    public sealed record EdgeRefDto(
        string FromNodeId,
        string FromPort,
        string ToNodeId);

    /// <summary>One rewired edge — before (removed) and after (inserted).</summary>
    /// <param name="Before">The edge removed by the patch.</param>
    /// <param name="After">The edge inserted by the patch.</param>
    public sealed record RewiredEdgeDto(
        EdgeRefDto Before,
        EdgeRefDto After);

    /// <summary>One parameter delta inside a re-parameterized node.</summary>
    /// <param name="Name">Parameter key (kind-declared name).</param>
    /// <param name="Before">Old value (null when added).</param>
    /// <param name="After">New value (null when removed).</param>
    public sealed record ParameterChangeDto(
        string Name,
        string? Before,
        string? After);

    /// <summary>One re-parameterized node: per-parameter deltas.</summary>
    /// <param name="NodeId">Graph-local id of the re-parameterized node.</param>
    /// <param name="Changes">Per-parameter deltas from base to patched.</param>
    public sealed record ReParameterizedNodeDto(
        string NodeId,
        IReadOnlyList<ParameterChangeDto> Changes);
}
