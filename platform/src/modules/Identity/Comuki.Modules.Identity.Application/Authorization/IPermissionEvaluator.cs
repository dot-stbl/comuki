using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Modules.Identity.Application.Authorization;

/// <summary>
/// Resolves a subject into effective permissions per scope, with a short
/// in-process cache. Grant and revoke handlers invalidate the subject's
/// entry on write, so the 30-second TTL is only a bound on staleness for
/// changes made outside this process.
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>Evaluates the subject's effective permissions (cached for 30s).</summary>
    /// <param name="subject"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<SubjectAuthorization> EvaluateAsync(RoleSubject subject, CancellationToken cancellationToken = default);

    /// <summary>Drops the subject's cached entry — called by grant/revoke handlers.</summary>
    /// <param name="subject"></param>
    public void Invalidate(RoleSubject subject);
}
