using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Identity.Domain.Assignments;

/// <summary>
/// One grant: subject × role × scope. Roles are code-declared; only the
/// grants are data. Revocation is a timestamp, not a delete — the audit
/// trail stays and the partial unique index only guards active rows.
/// </summary>
public sealed class RoleAssignment
{
    internal RoleAssignment()
    {
    }

    /// <summary>Strong-typed assignment id (UUIDv7).</summary>
    public RoleAssignmentId Id { get; private set; }

    /// <summary>Who holds the grant — a user or an API key.</summary>
    public SubjectType SubjectType { get; private set; }

    /// <summary>Raw subject id (user id or api key id).</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>The granted role (stored as its key).</summary>
    public Role Role { get; private set; }

    /// <summary>Platform or project axis of the grant.</summary>
    public ScopeLevel ScopeLevel { get; private set; }

    /// <summary>Project id; null exactly when <see cref="ScopeLevel"/> is platform.</summary>
    public ProjectId? ScopeProjectId { get; private set; }

    /// <summary>Who granted this; null for system/bootstrap grants.</summary>
    public SubjectType? GrantedByType { get; private set; }

    /// <summary>Raw granter id; null for system/bootstrap grants.</summary>
    public Guid? GrantedById { get; private set; }

    /// <summary>When the grant was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the grant was revoked; null while active.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Whether the grant currently contributes permissions.</summary>
    public bool IsActive => RevokedAt is null;

    /// <summary>The subject as a <see cref="RoleSubject"/>.</summary>
    public RoleSubject Subject => new(SubjectType, SubjectId);

    /// <summary>The granter as a <see cref="RoleSubject"/>, when recorded.</summary>
    public RoleSubject? GrantedBy => GrantedByType is { } type && GrantedById is { } id ? new RoleSubject(type, id) : null;

    /// <summary>Creates an active grant.</summary>
    /// <param name="subject"></param>
    /// <param name="role"></param>
    /// <param name="scope"></param>
    /// <param name="grantedBy"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">The scope level and project id disagree.</exception>
    public static RoleAssignment Create(
        RoleSubject subject,
        Role role,
        AssignmentScope scope,
        RoleSubject? grantedBy,
        DateTimeOffset now)
    {
        return scope.Level == ScopeLevel.Project && scope.ProjectId is not { }
            ? throw new ArgumentException("project scope requires a project id", nameof(scope))
            : new RoleAssignment
            {
                Id = RoleAssignmentId.New(),
                SubjectType = subject.Type,
                SubjectId = subject.Id,
                Role = role,
                ScopeLevel = scope.Level,
                ScopeProjectId = scope.Level == ScopeLevel.Project ? scope.ProjectId : null,
                GrantedByType = grantedBy?.Type,
                GrantedById = grantedBy?.Id,
                CreatedAt = now,
            };
    }

    /// <summary>Revokes the grant; idempotent.</summary>
    /// <param name="now"></param>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
