using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Attaches a Repository to a Project under a role + access pair (with an optional credential override).</summary>
/// <param name="ProjectId"></param>
/// <param name="RepositoryId"></param>
/// <param name="Role">Wire-form role string (trim + lower-case, invariant culture); open set.</param>
/// <param name="Access"></param>
/// <param name="CredentialOverrideRef">Optional per-attachment credential override ref (opaque integration key).</param>
public sealed record AttachRepositoryCommand(
    ProjectId ProjectId,
    RepositoryId RepositoryId,
    string Role,
    AttachmentAccess Access,
    string? CredentialOverrideRef);
