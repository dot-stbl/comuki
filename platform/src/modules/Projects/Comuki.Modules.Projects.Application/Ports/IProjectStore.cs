using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Ports;

/// <summary>
/// Persistence port for projects. Implemented by the module infrastructure
/// over its DbContext; Application code never touches EF. <see cref="AddAsync"/>
/// persists a project together with its default settings row in one unit of
/// work — a project without settings cannot exist through the write paths.
/// </summary>
public interface IProjectStore
{
    /// <summary>Finds a project by id.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Project?> FindByIdAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>Finds a project by its (normalized, lower-cased) slug.</summary>
    /// <param name="slug"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Lists projects ordered by creation time; archived ones unless <paramref name="includeArchived"/>.</summary>
    /// <param name="includeArchived"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>Persists a new project and its default settings row atomically.</summary>
    /// <param name="project"></param>
    /// <param name="settings"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddAsync(Project project, ProjectSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed project.</summary>
    /// <param name="project"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(Project project, CancellationToken cancellationToken = default);
}
