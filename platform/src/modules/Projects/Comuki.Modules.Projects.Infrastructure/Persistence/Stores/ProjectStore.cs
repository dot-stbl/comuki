using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    public async Task<int> CountAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = db.Projects.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(static project => !project.Archived);
        }

        return await query.CountAsync(cancellationToken);
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
    public async Task<bool> TryInsertWithProjectLimitAsync(
        Project project,
        ProjectSettings settings,
        int cap,
        CancellationToken cancellationToken = default)
    {
        if (cap <= 0)
        {
            // Cap of zero or less means no projects are allowed on this
            // edition — fail closed without touching the database.
            return false;
        }

        // Transaction-scoped advisory lock + count + insert run inside
        // one Postgres transaction. Two concurrent writers both reach
        // BeginTransaction; only the first acquires the lock; the second
        // blocks until the first commits (or rolls back), then sees the
        // first's insert in its count and bails. This is the authoritative
        // check — the request-time filter is a fast-fail advisory.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var connection = db.Database.GetDbConnection();
        // boundary: ADO contract — Connection is always set on a live transaction
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction.GetDbTransaction();
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtext('limit:projects'))";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // Count runs on the same DbContext / connection / transaction the
        // lock was acquired on, so it sees the first writer's inserts.
        var current = await db.Projects
            .AsNoTracking()
            .CountAsync(static project => !project.Archived, cancellationToken);

        if (current >= cap)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        db.Projects.Add(project);
        db.ProjectSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
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
