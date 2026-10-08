using System.Security.Claims;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Comuki.Modules.Identity.Infrastructure.Security.Authorization;

/// <summary>
/// The action axis of the authorization model, enforced once per request
/// for every MVC action: resolves the principal into an RBAC subject,
/// hands it to <see cref="IPermissionEvaluator"/>, and checks the
/// <see cref="RequiresPermissionAttribute"/> the endpoint carries. A
/// missing permission answers 403 <c>problem+json</c> with
/// <c>code=permission.denied</c>; an anonymous caller on a demanding
/// endpoint answers 401. The object axis is not this filter's business —
/// out-of-scope rows surface as 404 downstream. Minimal-API endpoints are
/// covered by <see cref="RequiresPermissionMiddleware"/>; both share
/// <see cref="PermissionGate"/> so the decision exists once.
/// </summary>
/// <param name="evaluator"></param>
/// <remarks>
/// A resource filter rather than an authorization filter deliberately:
/// this filter owns the whole check inline, and the resource stage wraps
/// model binding and result execution, so the decision cannot be
/// bypassed by an earlier short-circuit. The object axis is enforced
/// elsewhere — the ambient subject scope feeds the row-level query
/// filters, and out-of-scope rows surface as 404 downstream.
/// </remarks>
public sealed class RequiresPermissionFilter(IPermissionEvaluator evaluator) : IAsyncResourceFilter
{
    /// <summary>
    /// The one deny this filter produces. A literal beside the
    /// ProblemDetails body it lands in — the contract is stable strings.
    /// </summary>
    public const string PermissionDeniedCode = "permission.denied";

    /// <summary>
    /// The 401 code for an anonymous caller on a demanding endpoint.
    /// </summary>
    public const string AuthenticationRequiredCode = "authentication.required";

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Last wins — endpoint metadata is ordered least to most specific
        // (controller attributes before action attributes), matching the
        // framework's own reader.
        var demand = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiresPermissionAttribute>()
            .LastOrDefault();

        if (demand is null || await PermissionGate.EvaluateAsync(
                context.HttpContext.User,
                evaluator,
                demand.PermissionKey,
                context.HttpContext.RequestAborted) is not { } denial)
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(denial.Problem)
        {
            StatusCode = denial.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>
/// The one permission decision shared by the MVC resource filter and the
/// minimal-API middleware: subject resolution plus evaluation, producing
/// the canonical 401 / 403 problem or null when the demand is satisfied.
/// </summary>
internal static class PermissionGate
{
    /// <summary>Evaluates the demand; null = allowed.</summary>
    /// <param name="principal">Request principal.</param>
    /// <param name="evaluator">RBAC evaluator.</param>
    /// <param name="permissionKey">Demanded key.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<PermissionDenial?> EvaluateAsync(
        ClaimsPrincipal principal,
        IPermissionEvaluator evaluator,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        if (RequiresPermissionSubjectResolver.Resolve(principal) is not { } subject)
        {
            return RequiresPermissionDenialFactory.Denial(
                StatusCodes.Status401Unauthorized,
                RequiresPermissionFilter.AuthenticationRequiredCode,
                $"permission '{permissionKey}' requires an authenticated subject");
        }

        var authorization = await evaluator.EvaluateAsync(subject, cancellationToken);
        return authorization.IsPermitted(new PermissionKey(permissionKey))
            ? null
            : RequiresPermissionDenialFactory.Denial(
                StatusCodes.Status403Forbidden,
                RequiresPermissionFilter.PermissionDeniedCode,
                $"permission '{permissionKey}' is required");
    }

    // Subject resolution moved to <see cref="RequiresPermissionSubjectResolver"/>.
}

/// <summary>One deny decision: the status plus the ready ProblemDetails body.</summary>
/// <param name="StatusCode">401 (anonymous) or 403 (missing permission).</param>
/// <param name="Problem">ProblemDetails body to serialize.</param>
internal sealed record PermissionDenial(int StatusCode, ProblemDetails Problem);

/// <summary>
/// Permission-denial problem builder used by
/// <see cref="RequiresPermissionFilter"/>. Extracted per the
/// no-private-methods rule: the title defaults and
/// <c>"code"</c> extension shape must be the same for every denial.
/// </summary>
file static class RequiresPermissionDenialFactory
{
    /// <summary>
    /// Builds the deny envelope: status 401 for an anonymous caller, 403
    /// when authenticated but missing the required permission. Title
    /// picks by status so the openapi / problem-details transformer can
    /// stay in charge of the canonical shape (issue #20).
    /// </summary>
    /// <param name="statusCode">401 or 403.</param>
    /// <param name="code">Stable machine code: <c>"auth.unauthenticated"</c> or <c>"permission.denied"</c>.</param>
    /// <param name="detail">Human-readable detail; safe — no PII, no stack.</param>
    public static PermissionDenial Denial(int statusCode, string code, string detail)
    {
        var typed = TypedResults.Problem(
            title: statusCode == StatusCodes.Status403Forbidden ? "Permission denied" : "Authentication required",
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

        return new PermissionDenial(statusCode, typed.ProblemDetails);
    }
}

/// <summary>
/// Subject resolution for <see cref="RequiresPermissionFilter"/>:
/// principal → <see cref="RoleSubject"/>. An API-key principal carries
/// the api-key claim and resolves to its own subject; otherwise the
/// <see cref="ClaimTypes.NameIdentifier"/> claim resolves to the user
/// subject. Unresolvable principals (anonymous, foreign) return null —
/// a demand plus no subject is a 401, never a pass.
/// </summary>
file static class RequiresPermissionSubjectResolver
{
    /// <summary>
    /// Resolves the bearer subject: API-key claim wins, otherwise the
    /// nameidentifier claim; null means the principal is anonymous (or
    /// carries a claim shape this build doesn't understand — the
    /// demand is denied as 401).
    /// </summary>
    /// <param name="principal">The bearer principal carried in the request.</param>
    public static RoleSubject? Resolve(ClaimsPrincipal principal)
    {
        return OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
            ?? OfClaim(ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    /// <summary>
    /// Read one named claim as a Guid and wrap it in a
    /// <see cref="RoleSubject"/>; null on missing / empty / non-guid.
    /// </summary>
    public static RoleSubject? OfClaim(string claimName, SubjectType type, ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }
}
