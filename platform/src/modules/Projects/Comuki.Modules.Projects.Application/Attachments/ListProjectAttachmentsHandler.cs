using Comuki.Modules.Projects.Application.Views;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Lists every attachment of one Project, oldest first.</summary>
/// <param name="attachments">Attachment persistence port.</param>
/// <param name="mapper">Mapperly-driven view projections (stateless, singleton).</param>
public sealed class ListProjectAttachmentsHandler(
    IProjectRepositoryAttachmentStore attachments,
    IProjectsMapper mapper)
{
    /// <summary>Returns the attachment list as views.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// 
    public async Task<IReadOnlyList<ProjectRepositoryAttachmentView>> HandleAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var listed = await attachments.ListByProjectAsync(projectId, cancellationToken);

        return [.. listed.Select(mapper.ToView)];
    }
}
