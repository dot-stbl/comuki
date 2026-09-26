using Comuki.Modules.Projects.Application.Attachments;
using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IProjectRepositoryAttachmentStore"/> over
/// the scoped <see cref="ProjectsDbContext"/>. Single-row finds track
/// (unit-of-work: find → mutate → save in one scope, the same pattern as
/// <see cref="DbDomainTypeAdmissionStore"/>); the list reads are no-tracking.
/// </summary>
/// <param name="db">Projects context of the current scope.</param>
public sealed class DbProjectRepositoryAttachmentStore(ProjectsDbContext db) : IProjectRepositoryAttachmentStore
{
    /// <inheritdoc />
    public async Task<ProjectRepositoryAttachment?> FindAsync(
        ProjectId projectId,
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default)
    {
        return await db.ProjectRepositoryAttachments.SingleOrDefaultAsync(
            attachment => attachment.ProjectId == projectId && attachment.RepositoryId == repositoryId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRepositoryAttachment>> ListByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        return await db.ProjectRepositoryAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ProjectId == projectId)
            .OrderBy(static attachment => attachment.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRepositoryAttachment>> ListByRepositoryAsync(
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default)
    {
        return await db.ProjectRepositoryAttachments
            .AsNoTracking()
            .Where(attachment => attachment.RepositoryId == repositoryId)
            .OrderBy(static attachment => attachment.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(ProjectRepositoryAttachment attachment, CancellationToken cancellationToken = default)
    {
        // New attachments are detached aggregates: Add marks them Added instead
        // of Update treating them as an existing row.
        db.ProjectRepositoryAttachments.Add(attachment);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ProjectRepositoryAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (db.Entry(attachment).State is EntityState.Detached)
        {
            db.ProjectRepositoryAttachments.Update(attachment);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        ProjectId projectId,
        RepositoryId repositoryId,
        CancellationToken cancellationToken = default)
    {
        // Load-then-remove rather than ExecuteDelete: the row must be seen
        // through the context's subject-scope query filter, and the caller
        // wants to know whether it existed.
        if (await db.ProjectRepositoryAttachments.SingleOrDefaultAsync(
                attachment => attachment.ProjectId == projectId && attachment.RepositoryId == repositoryId,
                cancellationToken) is not { } row)
        {
            return false;
        }

        db.ProjectRepositoryAttachments.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
