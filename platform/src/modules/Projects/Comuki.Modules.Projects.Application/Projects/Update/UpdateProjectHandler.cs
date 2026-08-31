using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>Applies a partial update to an existing project.</summary>
/// <param name="projects"></param>
/// <param name="clock"></param>
public sealed class UpdateProjectHandler(IProjectStore projects, TimeProvider clock)
{
    /// <summary>Updates the project.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ProjectNotFoundException">No project with the given id.</exception>
    public async Task<ProjectView> HandleAsync(UpdateProjectCommand command, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new ProjectNotFoundException(command.ProjectId);

        project.Update(
            command.Name,
            command.Description,
            command.ProfilesGitUrl,
            command.ProfilesGitRef,
            clock.GetUtcNow());

        await projects.SaveAsync(project, cancellationToken);

        return ProjectMapper.ToView(project);
    }
}
