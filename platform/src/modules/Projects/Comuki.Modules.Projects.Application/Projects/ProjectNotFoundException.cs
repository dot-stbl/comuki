using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects;

/// <summary>The referenced project does not exist (or has no settings row); maps to HTTP 404.</summary>
/// <param name="projectId"></param>
public sealed class ProjectNotFoundException(ProjectId projectId)
    : Exception($"project '{projectId}' not found")
{
    /// <summary>Project that was looked up.</summary>
    public ProjectId ProjectId { get; } = projectId;
}
