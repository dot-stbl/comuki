using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IRoleAssignmentStore"/>. Active-only
/// reads: a revoked assignment never contributes permissions again.
/// </summary>
/// <param name="db"></param>
public sealed class RoleAssignmentStore(IdentityDbContext db) : IRoleAssignmentStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleAssignment>> ListActiveAsync(
        RoleSubject subject,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.RoleAssignments
            .Where(assignment => assignment.SubjectType == subject.Type
                && assignment.SubjectId == subject.Id
                && assignment.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<RoleAssignment?> FindActiveAsync(
        RoleAssignmentId assignmentId,
        CancellationToken cancellationToken = default)
    {
        return await db.RoleAssignments.SingleOrDefaultAsync(
            assignment => assignment.Id == assignmentId && assignment.RevokedAt == null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RoleAssignment?> FindActiveAsync(
        RoleSubject subject,
        Role role,
        AssignmentScope scope,
        CancellationToken cancellationToken = default)
    {
        return await db.RoleAssignments.SingleOrDefaultAsync(
            assignment => assignment.SubjectType == subject.Type
                && assignment.SubjectId == subject.Id
                && assignment.Role == role
                && assignment.ScopeLevel == scope.Level
                && assignment.ScopeProjectId == scope.ProjectId
                && assignment.RevokedAt == null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(RoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        if (db.Entry(assignment).State == EntityState.Detached)
        {
            _ = db.RoleAssignments.Add(assignment);
        }

        _ = await db.SaveChangesAsync(cancellationToken);
    }
}
