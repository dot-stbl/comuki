using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Modules.Identity.Application.Assignments.Grant;

/// <summary>
/// Grants a role to a subject at a scope. <paramref name="ActingAs"/>
/// carries the grantor for the escalation check; null means a
/// system/bootstrap path (migrator seed), which bypasses seniority.
/// </summary>
/// <param name="Grantee"></param>
/// <param name="Role"></param>
/// <param name="Scope"></param>
/// <param name="ActingAs"></param>
public sealed record GrantRoleCommand(
    RoleSubject Grantee,
    Role Role,
    AssignmentScope Scope,
    RoleSubject? ActingAs);
