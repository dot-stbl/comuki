using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Read model of a project repository attachment — everything the operational UI needs, nothing internal.</summary>
/// <param name="Id">Attachment id.</param>
/// <param name="ProjectId">Owning project.</param>
/// <param name="RepositoryId">Attached repository (the same <see cref="Guid"/> bytes the Repositories module uses).</param>
/// <param name="Role">Wire-form role string (trim + lower-case, invariant culture); the open set means it is a string, not an enum.</param>
/// <param name="Access">Declared access level.</param>
/// <param name="CredentialOverrideRef">Optional per-attachment credential override ref.</param>
/// <param name="CreatedAt"></param>
/// <param name="UpdatedAt"></param>
public sealed record ProjectRepositoryAttachmentView(
    ProjectRepositoryAttachmentId Id,
    ProjectId ProjectId,
    RepositoryId RepositoryId,
    string Role,
    AttachmentAccess Access,
    string? CredentialOverrideRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
