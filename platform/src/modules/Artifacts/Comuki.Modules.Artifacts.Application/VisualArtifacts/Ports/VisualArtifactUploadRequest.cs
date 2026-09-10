using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;

/// <summary>
/// One visual-artifact upload — the parameters the service hands to
/// the store. Body is consumed by the store and disposed by the
/// caller once the call returns.
/// </summary>
/// <param name="ProjectId">Owning project.</param>
/// <param name="Filename">Original filename.</param>
/// <param name="ContentType">MIME type (validated against <see cref="VisualArtifactLimits"/>).</param>
/// <param name="SizeBytes">Pre-counted body length (the store enforces this against MinIO's PutObject).</param>
/// <param name="Body">Object body — read to EOF by the store.</param>
/// <param name="CreatedBy">Who published — <c>worker</c> or <c>brain</c>.</param>
/// <param name="CreatedAt">Wall-clock the row is stamped with (UTC).</param>
/// <param name="RunId">Optional link to a run.</param>
/// <param name="WorkItemId">Optional link to a work item.</param>
/// <param name="SessionId">Optional link to a chat session (slice 4).</param>
/// <param name="TicketId">Optional link to a ticket.</param>
/// <param name="Title">Optional human-readable title.</param>
/// <param name="ExplicitId">
/// Optional id the publisher wants to reuse — when set, the store bumps
/// the artifact's version instead of minting a fresh id.
/// </param>
public sealed record VisualArtifactUploadRequest(
    ProjectId ProjectId,
    string Filename,
    string ContentType,
    long SizeBytes,
    Stream Body,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? RunId = null,
    Guid? WorkItemId = null,
    Guid? SessionId = null,
    Guid? TicketId = null,
    string? Title = null,
    VisualArtifactId? ExplicitId = null);
