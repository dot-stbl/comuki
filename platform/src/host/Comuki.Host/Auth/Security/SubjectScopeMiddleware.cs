using System.Security.Claims;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Shared.Kernel.Scoping;

namespace Comuki.Host.Auth.Security;

/// <summary>
/// Establishes the ambient subject scope for every request, right after
/// authentication: a principal that resolves to an RBAC subject gets the
/// object-axis projection of its (cached) authorization; anything else —
/// an anonymous caller, a foreign principal — gets the fail-closed
/// Nothing scope, so an open endpoint that reaches a scoped query answers
/// empty, never everything. Worker-token surfaces re-establish
/// <c>AsSystem("worker-runtime")</c> themselves after their own gate.
/// </summary>
public sealed class SubjectScopeMiddleware(RequestDelegate next)
{
    /// <summary>Installs the scope for the rest of the request pipeline.</summary>
    /// <param name="context"></param>
    public async Task InvokeAsync(HttpContext context)
    {
        var accessor = context.RequestServices.GetRequiredService<ISubjectScopeAccessor>();

        if (SubjectScopePrincipal.Resolve(context.User) is not { } subject)
        {
            using (accessor.Begin(SubjectScope.Nothing))
            {
                await next(context);
            }

            return;
        }

        var evaluator = context.RequestServices.GetRequiredService<IPermissionEvaluator>();
        var authorization = await evaluator.EvaluateAsync(subject, context.RequestAborted);

        using (accessor.Begin(authorization.ToSubjectScope()))
        {
            await next(context);
        }
    }
}

/// <summary>
/// Principal → <see cref="RoleSubject"/> for the scope middleware: the
/// api-key claim resolves to the key subject, otherwise the
/// nameidentifier claim resolves to the user subject. Mirrors the
/// resolver the enforcement filter and the auth controller use; kept
/// local because those copies are file-private by design.
/// </summary>
file static class SubjectScopePrincipal
{
    public static RoleSubject? Resolve(ClaimsPrincipal principal)
    {
        return OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
            ?? OfClaim(ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    public static RoleSubject? OfClaim(string claimName, SubjectType type, ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }
}
