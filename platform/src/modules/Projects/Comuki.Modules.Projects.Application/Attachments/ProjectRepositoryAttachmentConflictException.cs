using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// A (project, repository) attachment pair is already taken; maps to HTTP 409.
/// Both ids are named in the message so a human reader can spot the conflict
/// without re-reading the request body.
/// </summary>
public sealed class ProjectRepositoryAttachmentConflictException(ProjectId projectId, RepositoryId repositoryId)
    : Exception($"project '{projectId}' already has an attachment for repository '{repositoryId}'")
{
    /// <summary>Project of the conflicting attachment.</summary>
    public ProjectId ProjectId { get; } = projectId;

    /// <summary>Repository of the conflicting attachment.</summary>
    public RepositoryId RepositoryId { get; } = repositoryId;
}
