using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Modules.Identity.Application.Assignments.Revoke;

/// <summary>
/// Revokes one active assignment. <paramref name="ActingAs"/> obeys the
/// same seniority rule as granting — whoever can create an assignment
/// can also remove it, no one-way doors.
/// </summary>
/// <param name="AssignmentId"></param>
/// <param name="ActingAs"></param>
public sealed record RevokeRoleCommand(RoleAssignmentId AssignmentId, RoleSubject? ActingAs);
