using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Projects.Update;

/// <summary>Applies a partial update to an existing project.</summary>
public sealed class UpdateProjectHandler(IProjectStore projects, TimeProvider clock, IProjectsMapper mapper)
{
    /// <summary>Updates the project.</summary>
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
            clock.GetUtcNow(),
            command.Icon,
            command.Color,
            command.Tags);

        await projects.SaveAsync(project, cancellationToken);

        return mapper.ToView(project);
    }
}
