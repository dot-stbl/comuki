using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Projects.Archive;

/// <summary>Archives a project; archiving twice is a no-op.</summary>
/// <param name="projects"></param>
/// <param name="clock"></param>
public sealed class ArchiveProjectHandler(IProjectStore projects, TimeProvider clock)
{
    /// <summary>Archives the project.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ProjectNotFoundException">No project with the given id.</exception>
    public async Task<ProjectView> HandleAsync(ArchiveProjectCommand command, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new ProjectNotFoundException(command.ProjectId);

        project.Archive(clock.GetUtcNow());
        await projects.SaveAsync(project, cancellationToken);

        return ProjectMapper.ToView(project);
    }
}
