using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Riok.Mapperly.Abstractions;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Mapperly-generated implementation of <see cref="IProjectsMapper"/>.
/// Strict target mapping: every view property must have a same-name
/// same-type source — an unmapped property is a build error (RMG020),
/// never a silently defaulted field.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public sealed partial class ProjectsMapper : IProjectsMapper
{
    /// <summary>Maps a project entity to its read model.</summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public partial ProjectView ToView(Project source);

    /// <summary>Maps a settings entity to its read model.</summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public partial ProjectSettingsView ToView(ProjectSettings source);
}
