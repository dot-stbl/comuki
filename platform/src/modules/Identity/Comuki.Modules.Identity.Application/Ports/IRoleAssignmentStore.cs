using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Modules.Identity.Application.Ports;

/// <summary>
/// Persistence port for role assignments — the only write path for
/// grants and the only read path of the evaluator.
/// </summary>
public interface IRoleAssignmentStore
{
    /// <summary>Lists every active assignment of a subject.</summary>
    /// <param name="subject"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<RoleAssignment>> ListActiveAsync(RoleSubject subject, CancellationToken cancellationToken = default);

    /// <summary>Finds one active assignment by id.</summary>
    /// <param name="assignmentId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<RoleAssignment?> FindActiveAsync(RoleAssignmentId assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Finds an active assignment of a subject for a role at a scope — the duplicate guard.</summary>
    /// <param name="subject"></param>
    /// <param name="role"></param>
    /// <param name="scope"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<RoleAssignment?> FindActiveAsync(
        RoleSubject subject,
        Role role,
        AssignmentScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed assignment.</summary>
    /// <param name="assignment"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(RoleAssignment assignment, CancellationToken cancellationToken = default);
}
