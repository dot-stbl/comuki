using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Projects.Queries;

/// <summary>Lists projects; archived ones only on request.</summary>
/// <param name="projects"></param>
/// <param name="mapper">Projects entities onto their API views.</param>
public sealed class ListProjectsHandler(IProjectStore projects, IProjectsMapper mapper)
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

        return [.. listed.Select(mapper.ToView)];
    }
}
