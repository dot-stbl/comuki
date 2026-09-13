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
        if (ResolveSubject(principal) is not { } subject)
        {
            return Denial(
                StatusCodes.Status401Unauthorized,
                RequiresPermissionFilter.AuthenticationRequiredCode,
                $"permission '{permissionKey}' requires an authenticated subject");
        }

        var authorization = await evaluator.EvaluateAsync(subject, cancellationToken);
        return authorization.IsPermitted(new PermissionKey(permissionKey))
            ? null
            : Denial(
                StatusCodes.Status403Forbidden,
                RequiresPermissionFilter.PermissionDeniedCode,
                $"permission '{permissionKey}' is required");
    }

    /// <summary>
    /// Principal → <see cref="RoleSubject"/>: an API-key principal carries
    /// the api-key claim and resolves to its own subject; otherwise the
    /// nameidentifier claim resolves to the user subject. Unresolvable
    /// principals (anonymous, foreign) return null — a demand plus no
    /// subject is a 401, never a pass.
    /// </summary>
    private static RoleSubject? ResolveSubject(ClaimsPrincipal principal)
    {
        return OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
            ?? OfClaim(ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    private static RoleSubject? OfClaim(string claimName, SubjectType type, ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }

    private static PermissionDenial Denial(int statusCode, string code, string detail)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20).
        var typed = TypedResults.Problem(
            title: statusCode == StatusCodes.Status403Forbidden ? "Permission denied" : "Authentication required",
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

        return new PermissionDenial(statusCode, typed.ProblemDetails);
    }
}

/// <summary>One deny decision: the status plus the ready ProblemDetails body.</summary>
/// <param name="StatusCode">401 (anonymous) or 403 (missing permission).</param>
/// <param name="Problem">ProblemDetails body to serialize.</param>
internal sealed record PermissionDenial(int StatusCode, ProblemDetails Problem);
