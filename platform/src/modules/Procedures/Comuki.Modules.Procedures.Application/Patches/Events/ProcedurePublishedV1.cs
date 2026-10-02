using Comuki.Modules.Procedures.Domain.Ids;

namespace Comuki.Modules.Procedures.Application.Patches.Events;

/// <summary>
/// Integration event published through the platform's outbox when a
/// procedure version is published (task 3.2: "publish
/// <c>procedures.procedure.published.v1</c> through the existing
/// outbox"). Carries the from/to version ids so a downstream
/// subscriber can audit the change without re-reading the
/// procedure's history; the <see cref="PublishedBy"/> identity is
/// the human approver, never the drafter — the publication service
/// enforces that rule before the event is written.
/// 
/// <para>
/// For git-authored publications (no <see cref="PatchId"/>) the field
/// is null. Subscribers that do not care about the patch lineage can
/// ignore it; subscribers that do (Studio's history view, replay's
/// planned-vs-observed classifier at task 5.3) treat null as
/// "no patch, applied directly from the control-plane git".
/// </para>
/// </summary>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">The procedure's stable key inside the project.</param>
/// <param name="FromVersionId">The content-addressed id of the version the publication replaced (null on the first publication).</param>
/// <param name="ToVersionId">The content-addressed id of the newly published version.</param>
/// <param name="PatchId">The graph patch that produced the publication (null for git-authored publications).</param>
/// <param name="PublishedBy">The human identity that approved the publication.</param>
/// <param name="PublishedAt">When the publication was committed (UTC).</param>
public sealed record ProcedurePublishedV1(
    Guid ProjectId,
    string ProcedureKey,
    string? FromVersionId,
    string ToVersionId,
    GraphPatchId? PatchId,
    string PublishedBy,
    DateTimeOffset PublishedAt);
