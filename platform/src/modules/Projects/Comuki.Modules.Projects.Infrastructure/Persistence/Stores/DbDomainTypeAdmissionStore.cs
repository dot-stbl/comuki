using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IDomainTypeAdmissionStore"/> over the
/// scoped <see cref="ProjectsDbContext"/>. Single-row reads track (find →
/// mutate → save in one scope, the <see cref="ProjectStore"/> pattern);
/// the list read is no-tracking. Lookup keys are normalized the same way
/// the entity normalized them on write, so callers pass the domain type as
/// the user typed it.
/// </summary>
/// <param name="db">Projects context of the current scope.</param>
public sealed class DbDomainTypeAdmissionStore(ProjectsDbContext db) : IDomainTypeAdmissionStore
{
    /// <inheritdoc />
    public async Task<DomainTypeAdmission?> FindAsync(
        ProjectId projectId,
        string domainType,
        CancellationToken cancellationToken = default)
    {
        var normalized = DomainTypeAdmission.NormalizeKey(domainType);

        return await db.DomainTypeAdmissions.SingleOrDefaultAsync(
            admission => admission.ProjectId == projectId && admission.DomainType == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainTypeAdmission>> ListAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        return await db.DomainTypeAdmissions
            .AsNoTracking()
            .Where(admission => admission.ProjectId == projectId)
            .OrderBy(static admission => admission.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(DomainTypeAdmission admission, CancellationToken cancellationToken = default)
    {
        // New policies are detached aggregates: Add marks them Added instead
        // of Update treating them as an existing row.
        db.DomainTypeAdmissions.Add(admission);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(DomainTypeAdmission admission, CancellationToken cancellationToken = default)
    {
        if (db.Entry(admission).State is EntityState.Detached)
        {
            db.DomainTypeAdmissions.Update(admission);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        DomainTypeAdmissionId admissionId,
        CancellationToken cancellationToken = default)
    {
        // Load-then-remove rather than ExecuteDelete: the row must be seen
        // through the context's subject-scope query filter, and the caller
        // wants to know whether it existed.
        if (await db.DomainTypeAdmissions.SingleOrDefaultAsync(
                admission => admission.Id == admissionId,
                cancellationToken) is not { } row)
        {
            return false;
        }

        db.DomainTypeAdmissions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
