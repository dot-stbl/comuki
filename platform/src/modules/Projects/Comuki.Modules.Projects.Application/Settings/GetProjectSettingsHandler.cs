using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Settings;

/// <summary>Reads the settings of a project (always through the store port — never a cached startup copy).</summary>
/// <param name="settings"></param>
public sealed class GetProjectSettingsHandler(IProjectSettingsStore settings)
{
    /// <summary>Returns the settings view, version included (the client echoes it on the next PUT).</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ProjectNotFoundException">No settings row for the project.</exception>
    public async Task<ProjectSettingsView> HandleAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        var current = await settings.FindAsync(projectId, cancellationToken)
            ?? throw new ProjectNotFoundException(projectId);

        return ProjectMapper.ToView(current);
    }
}
