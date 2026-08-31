using System.Collections.Frozen;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Memory;

namespace Comuki.Modules.Identity.Application.Authorization;

/// <summary>
/// Default <see cref="IPermissionEvaluator"/>: one cached read of the
/// subject's active assignments, expanded through the compiled
/// <see cref="RoleMatrix"/> into per-scope permission sets. Scoped — it
/// depends on the scoped assignment store; the <see cref="IMemoryCache"/>
/// is shared (singleton) so invalidation is seen by every request.
/// </summary>
/// <param name="cache"></param>
/// <param name="assignments"></param>
public sealed class PermissionEvaluator(IMemoryCache cache, IRoleAssignmentStore assignments)
    : IPermissionEvaluator
{
    /// <summary>
    /// Upper bound on staleness of an authorization decision. A security
    /// bound, deliberately a constant: grant/revoke invalidates eagerly,
    /// and the TTL only covers edits made outside this process.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<SubjectAuthorization> EvaluateAsync(
        RoleSubject subject,
        CancellationToken cancellationToken = default)
    {
        var authorization = await cache.GetOrCreateAsync(CacheKey(subject), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var active = await assignments.ListActiveAsync(subject, cancellationToken);
            return SubjectAuthorizationFactory.From(active);
        });

        return authorization ?? SubjectAuthorization.Empty;
    }

    /// <inheritdoc />
    public void Invalidate(RoleSubject subject)
    {
        cache.Remove(CacheKey(subject));
    }

    private static string CacheKey(RoleSubject subject)
    {
        return $"identity:permissions:{SubjectTypeKeys.Key(subject.Type)}:{subject.Id}";
    }
}

/// <summary>
/// The one translation between assignment rows and the effective
/// permission sets — platform grants widen globally, project grants only
/// inside their project.
/// </summary>
file static class SubjectAuthorizationFactory
{
    public static SubjectAuthorization From(IReadOnlyList<RoleAssignment> rows)
    {
        if (rows.Count == 0)
        {
            return SubjectAuthorization.Empty;
        }

        var platform = new HashSet<PermissionKey>();
        var projects = new Dictionary<ProjectId, HashSet<PermissionKey>>();

        foreach (var row in rows)
        {
            var granted = RoleMatrix.PermissionsOf(row.Role);

            if (row.ScopeLevel == ScopeLevel.Platform)
            {
                platform.UnionWith(granted);
                continue;
            }

            // A project-scope row without a project id cannot exist through
            // the write paths (the domain factory refuses it); surfaced
            // loudly rather than silently widening or narrowing.
            if (row.ScopeProjectId is not { } project)
            {
                throw new InvalidOperationException(
                    $"assignment {row.Id} carries project scope without a project id");
            }

            if (!projects.TryGetValue(project, out var bucket))
            {
                bucket = [];
                projects[project] = bucket;
            }

            bucket.UnionWith(granted);
        }

        return new SubjectAuthorization(
            platform.ToFrozenSet(),
            projects.ToFrozenDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlySet<PermissionKey>)pair.Value.ToFrozenSet()));
    }
}
