namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// Detaches a Repository from a Project. Detaching one Project's attachment
/// leaves the Repository row and any other Project's attachment untouched —
/// spec scenario 2. A missing (project, repository) pair surfaces as
/// <see cref="ProjectRepositoryAttachmentNotFoundException"/>.
/// </summary>
/// <param name="attachments">Attachment persistence port.</param>
public sealed class DetachRepositoryHandler(IProjectRepositoryAttachmentStore attachments)
{
    /// <summary>Detaches the repository.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ProjectRepositoryAttachmentNotFoundException">No attachment for the (project, repository) pair.</exception>
    public async Task HandleAsync(
        DetachRepositoryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!await attachments.DeleteAsync(command.ProjectId, command.RepositoryId, cancellationToken))
        {
            throw new ProjectRepositoryAttachmentNotFoundException(command.ProjectId, command.RepositoryId);
        }
    }
}
