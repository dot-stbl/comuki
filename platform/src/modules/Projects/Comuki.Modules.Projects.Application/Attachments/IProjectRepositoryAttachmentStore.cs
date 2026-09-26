using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// Persistence port for <see cref="ProjectRepositoryAttachment"/>. Implemented
/// by the module infrastructure over its scoped <c>ProjectsDbContext</c>;
/// Application code never touches EF. The store is the only place that
/// knows about the unique (project, repository) index — handlers check the
/// precondition (no existing row for the pair) and let the index be the
/// last-line arbiter under concurrency.
/// </summary>
public interface IProjectRepositoryAttachmentStore
{
    /// <summary>Tracked find by the (project, repository) pair; null when no attachment exists.</summary>
    /// <param name="projectId"></param>
    /// <param name="repositoryId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<ProjectRepositoryAttachment?> FindAsync(
        ProjectId projectId,
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>No-tracking list of every attachment of one Project, oldest first.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<ProjectRepositoryAttachment>> ListByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    /// <summary>No-tracking list of every attachment pointing at one Repository, oldest first.</summary>
    /// <param name="repositoryId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<ProjectRepositoryAttachment>> ListByRepositoryAsync(
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new attachment. The unique (project, repository) index is the concurrency arbiter.</summary>
    /// <param name="attachment"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddAsync(ProjectRepositoryAttachment attachment, CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated attachment loaded through this port.</summary>
    /// <param name="attachment"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(ProjectRepositoryAttachment attachment, CancellationToken cancellationToken = default);

    /// <summary>Deletes the (project, repository) attachment; false when no row exists.</summary>
    /// <param name="projectId"></param>
    /// <param name="repositoryId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<bool> DeleteAsync(
        ProjectId projectId,
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default);
}
