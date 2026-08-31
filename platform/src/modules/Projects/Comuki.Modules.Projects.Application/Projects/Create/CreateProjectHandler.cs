using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;

namespace Comuki.Modules.Projects.Application.Projects.Create;

/// <summary>
/// Creates a project and its default settings row in one unit of work. A
/// duplicate slug is refused loudly; the unique index below backs the
/// check (a concurrent create loses with a DB error instead of a dupe).
/// </summary>
/// <param name="projects"></param>
/// <param name="clock"></param>
public sealed class CreateProjectHandler(IProjectStore projects, TimeProvider clock)
{
    /// <summary>Creates the project.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ProjectConflictException">The slug is already taken.</exception>
    public async Task<ProjectView> HandleAsync(CreateProjectCommand command, CancellationToken cancellationToken = default)
    {
        var slug = command.Slug.Trim().ToLowerInvariant();
        if (await projects.FindBySlugAsync(slug, cancellationToken) is not null)
        {
            throw new ProjectConflictException($"project slug '{slug}' is already taken");
        }

        var now = clock.GetUtcNow();
        var project = Project.Create(
            command.Name,
            slug,
            command.Description,
            command.ProfilesGitUrl,
            command.ProfilesGitRef,
            now);

        await projects.AddAsync(project, ProjectSettings.CreateDefaults(project.Id, now), cancellationToken);

        return ProjectMapper.ToView(project);
    }
}
