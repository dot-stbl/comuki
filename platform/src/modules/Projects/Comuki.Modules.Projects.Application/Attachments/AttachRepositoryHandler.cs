using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Domain.Attachments;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// Attaches a Repository to a Project under a role + access pair. The
/// pre-flight guards the unique (project, repository) pair with an explicit
/// conflict exception so the handler reads cleanly; the unique index is the
/// last-line arbiter under concurrency. The project must exist
/// (<see cref="IProjectStore.FindByIdAsync"/>) — referencing a missing
/// project surfaces as <see cref="ProjectNotFoundException"/>.
/// </summary>
/// <param name="attachments">Attachment persistence port.</param>
/// <param name="projects">Project persistence port.</param>
/// <param name="clock">Time source for the attachment's created/updated timestamps.</param>
public sealed class AttachRepositoryHandler(
    IProjectRepositoryAttachmentStore attachments,
    IProjectStore projects,
    TimeProvider clock)
{
    /// <summary>Attaches the repository.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The created attachment's view.</returns>
    /// <exception cref="ProjectNotFoundException">The project does not exist.</exception>
    /// <exception cref="ProjectRepositoryAttachmentConflictException">A (project, repository) attachment already exists.</exception>
    public async Task<ProjectRepositoryAttachmentView> HandleAsync(
        AttachRepositoryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await projects.FindByIdAsync(command.ProjectId, cancellationToken) is null)
        {
            throw new ProjectNotFoundException(command.ProjectId);
        }

        if (await attachments.FindAsync(command.ProjectId, command.RepositoryId, cancellationToken) is not null)
        {
            throw new ProjectRepositoryAttachmentConflictException(command.ProjectId, command.RepositoryId);
        }

        var attachment = ProjectRepositoryAttachment.Create(
            command.ProjectId,
            command.RepositoryId,
            command.Role,
            command.Access,
            command.CredentialOverrideRef,
            clock.GetUtcNow());

        await attachments.AddAsync(attachment, cancellationToken);

        return ProjectRepositoryAttachmentMapper.ToView(attachment);
    }
}
