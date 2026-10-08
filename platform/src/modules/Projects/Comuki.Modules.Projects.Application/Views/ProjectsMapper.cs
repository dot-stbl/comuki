using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Riok.Mapperly.Abstractions;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Mapperly-generated implementation of <see cref="IProjectsMapper"/>.
/// Strict target mapping: every view property must have a same-name
/// same-type source — an unmapped property is a build error (RMG020),
/// never a silently defaulted field.
/// <para>
/// The attachment <c>Role</c> is the smart-type <see cref="AttachmentRole"/>
/// on the entity and a <see cref="string"/> on the view; the
/// <c>[MapProperty]</c> attribute tells the generator to call
/// <c>Role.Value</c> for the projection (no other shape-mismatch on the
/// row).
/// </para>
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public sealed partial class ProjectsMapper : IProjectsMapper
{
    /// <summary>Maps a project entity to its read model.</summary>
    /// 
    public partial ProjectView ToView(Project source);

    /// <summary>Maps a settings entity to its read model.</summary>
    /// 
    public partial ProjectSettingsView ToView(ProjectSettings source);

    /// <summary>Maps a project repository attachment entity to its read model.</summary>
    /// 
    [MapProperty(nameof(ProjectRepositoryAttachment.Role), nameof(ProjectRepositoryAttachmentView.Role), Use = nameof(RoleWire))]
    public partial ProjectRepositoryAttachmentView ToView(ProjectRepositoryAttachment source);

    private static string RoleWire(AttachmentRole role)
    {
        return role.Value;
    }
}
