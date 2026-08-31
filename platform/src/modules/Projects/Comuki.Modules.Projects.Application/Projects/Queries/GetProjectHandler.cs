using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects.Queries;

/// <summary>Reads a single project by id.</summary>
/// <param name="projects"></param>
public sealed class GetProjectHandler(IProjectStore projects)
{
    /// <summary>Returns the project view.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ProjectNotFoundException">No project with the given id.</exception>
    public async Task<ProjectView> HandleAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByIdAsync(projectId, cancellationToken)
            ?? throw new ProjectNotFoundException(projectId);

        return ProjectMapper.ToView(project);
    }
}
