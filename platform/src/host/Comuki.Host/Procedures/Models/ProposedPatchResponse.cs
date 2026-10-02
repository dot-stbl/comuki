namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Response DTO for a proposed patch
/// (<c>POST /api/v1/procedures/{projectId}/{procedureKey}/propose-patch</c>).
/// The chat surface has no publication path — the human publishes from
/// Studio after reviewing the rendered diff.
/// </summary>
public sealed record ProposedPatchResponse(
    string PatchId,
    string BaseVersionId,
    string Rationale,
    string DiffSummary);
