using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IProjectStore"/> over the scoped
/// <see cref="ProjectsDbContext"/>. Finds track (unit of work: find →
/// mutate → save in one scope, the Identity store pattern); the list read
/// is no-tracking.
/// </summary>
/// <param name="db"></param>
public sealed class ProjectStore(ProjectsDbContext db) : IProjectStore
{
    /// <inheritdoc />
    public async Task<Project?> FindByIdAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return await db.Projects.SingleOrDefaultAsync(project => project.Id == projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();

        return await db.Projects.SingleOrDefaultAsync(project => project.Slug == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListAsync(
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = db.Projects.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(static project => !project.Archived);
        }

        return await query.OrderBy(static project => project.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        Project project,
        ProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        db.Projects.Add(project);
        db.ProjectSettings.Add(settings);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (db.Entry(project).State == EntityState.Detached)
        {
            db.Projects.Add(project);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
