using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects;

/// <summary>The referenced project does not exist (or has no settings row); maps to HTTP 404.</summary>
public sealed class ProjectNotFoundException(ProjectId projectId)
    : DomainException(ErrorCode, $"project '{projectId}' not found")
{
    private const string ErrorCode = "project.not_found";

    /// <summary>Project that was looked up.</summary>
    public ProjectId ProjectId { get; } = projectId;
}
