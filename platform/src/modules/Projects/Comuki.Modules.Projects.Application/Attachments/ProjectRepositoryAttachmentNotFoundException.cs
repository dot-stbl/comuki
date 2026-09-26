using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>No attachment exists for the (project, repository) pair; maps to HTTP 404.</summary>
/// <param name="projectId"></param>
/// <param name="repositoryId"></param>
public sealed class ProjectRepositoryAttachmentNotFoundException(ProjectId projectId, RepositoryId repositoryId)
    : Exception($"project '{projectId}' has no attachment for repository '{repositoryId}'")
{
    /// <summary>Project of the missing attachment.</summary>
    public ProjectId ProjectId { get; } = projectId;

    /// <summary>Repository of the missing attachment.</summary>
    public RepositoryId RepositoryId { get; } = repositoryId;
}
