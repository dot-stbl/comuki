using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Lists every attachment of one Project, oldest first.</summary>
/// <param name="attachments"></param>
public sealed class ListProjectAttachmentsHandler(IProjectRepositoryAttachmentStore attachments)
{
    /// <summary>Returns the attachment list as views.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ProjectRepositoryAttachmentView>> HandleAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var listed = await attachments.ListByProjectAsync(projectId, cancellationToken);

        return [.. listed.Select(static attachment => ProjectRepositoryAttachmentMapper.ToView(attachment))];
    }
}
