using System.Collections.Frozen;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;

namespace Comuki.Modules.Identity.Application.Authorization;

/// <summary>
/// What a subject may do and where — the single answer both the
/// enforcement filter and any scope-aware read path consult. Permissions
/// never ride in the cookie or the API key; they are recomputed per
/// request (cached briefly) so a revoke lands within seconds.
/// </summary>
/// <param name="PlatformPermissions">Permissions held at platform scope — valid everywhere.</param>
/// <param name="ProjectPermissions">Permissions held per project scope.</param>
public sealed record SubjectAuthorization(
    IReadOnlySet<PermissionKey> PlatformPermissions,
    IReadOnlyDictionary<ProjectId, IReadOnlySet<PermissionKey>> ProjectPermissions)
{
    /// <summary>The fail-closed answer for a subject with no active assignments.</summary>
    public static readonly SubjectAuthorization Empty = new(
        FrozenSet<PermissionKey>.Empty,
        FrozenDictionary<ProjectId, IReadOnlySet<PermissionKey>>.Empty);

    /// <summary>Whether the key is held at platform scope.</summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool IsPermittedGlobally(PermissionKey key)
    {
        return PlatformPermissions.Contains(key);
    }

    /// <summary>Whether the key is held anywhere (platform or any project).</summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public bool IsPermitted(PermissionKey key)
    {
        return IsPermittedGlobally(key)
            || ProjectPermissions.Values.Any(set => set.Contains(key));
    }

    /// <summary>
    /// Whether the key is held for the given project — platform scope
    /// covers every project; a project scope covers exactly its own.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="project"></param>
    /// <returns></returns>
    public bool IsPermittedIn(PermissionKey key, ProjectId project)
    {
        return IsPermittedGlobally(key)
            || (ProjectPermissions.TryGetValue(project, out var permissions) && permissions.Contains(key));
    }

    /// <summary>
    /// The object-axis projection of this authorization: any
    /// platform-scope grant makes the subject unrestricted (its
    /// permissions apply in every project); otherwise the subject is
    /// confined to the projects its assignments reach. A subject with no
    /// assignments projects to <see cref="SubjectScope.Nothing"/> — the
    /// fail-closed answer.
    /// </summary>
    /// <returns>The scope to install for the rest of the request.</returns>
    public SubjectScope ToSubjectScope()
    {
        return PlatformPermissions.Count > 0
            ? new SubjectScope(Unrestricted: true, SystemName: null, ProjectIds: [])
            : new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [.. ProjectPermissions.Keys]);
    }
}
