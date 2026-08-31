using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Projects.Queries;

/// <summary>Lists projects; archived ones only on request.</summary>
/// <param name="projects"></param>
public sealed class ListProjectsHandler(IProjectStore projects)
{
    /// <summary>Returns the project list ordered by creation time.</summary>
    /// <param name="includeArchived"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ProjectView>> HandleAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var listed = await projects.ListAsync(includeArchived, cancellationToken);

        return [.. listed.Select(static project => ProjectMapper.ToView(project))];
    }
}
