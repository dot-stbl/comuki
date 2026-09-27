using Comuki.Modules.Projects.Domain.Attachments;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>Lists every attachment pointing at one Repository, oldest first — the "which projects hold this repo" view.</summary>
/// <param name="attachments"></param>
public sealed class ListRepositoryAttachmentsHandler(IProjectRepositoryAttachmentStore attachments)
{
    /// <summary>Returns the attachment list as views.</summary>
    /// <param name="repositoryId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ProjectRepositoryAttachmentView>> HandleAsync(
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default)
    {
        var listed = await attachments.ListByRepositoryAsync(repositoryId, cancellationToken);

        return [.. listed.Select(static attachment => ProjectRepositoryAttachmentMapper.ToView(attachment))];
    }
}
