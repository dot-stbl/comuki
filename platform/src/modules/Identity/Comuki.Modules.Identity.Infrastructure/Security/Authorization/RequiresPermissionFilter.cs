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
/// out-of-scope rows surface as 404 downstream.
/// </summary>
/// <param name="evaluator"></param>
/// <remarks>
/// A resource filter rather than an authorization filter deliberately:
/// this filter owns the whole check inline (no ambient scope in the
/// Comuki model — scope filtering happens in the reads themselves), and
/// the resource stage wraps model binding and result execution, so the
/// decision cannot be bypassed by an earlier short-circuit.
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

        if (demand is null)
        {
            _ = await next();

            return;
        }

        if (PrincipalSubjectResolver.Resolve(context.HttpContext.User) is not { } subject)
        {
            context.Result = PermissionProblem.Result(
                StatusCodes.Status401Unauthorized,
                AuthenticationRequiredCode,
                $"permission '{demand.PermissionKey}' requires an authenticated subject");
        }
        else if (!authorizationAllows(await evaluator.EvaluateAsync(subject, context.HttpContext.RequestAborted), demand.PermissionKey))
        {
            context.Result = PermissionProblem.Result(
                StatusCodes.Status403Forbidden,
                PermissionDeniedCode,
                $"permission '{demand.PermissionKey}' is required");
        }
        else
        {
            _ = await next();
        }
    }

    private static bool authorizationAllows(SubjectAuthorization authorization, string permissionKey)
    {
        return authorization.IsPermitted(new PermissionKey(permissionKey));
    }
}

/// <summary>
/// Principal → <see cref="RoleSubject"/>: an API-key principal carries
/// the api-key claim and resolves to its own subject; otherwise the
/// nameidentifier claim resolves to the user subject. Unresolvable
/// principals (anonymous, foreign) return null — a demand plus no
/// subject is a 401, never a pass.
/// </summary>
file static class PrincipalSubjectResolver
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

/// <summary>Builds the 401/403 ProblemDetails results this filter returns.</summary>
file static class PermissionProblem
{
    public static IActionResult Result(int statusCode, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == StatusCodes.Status403Forbidden ? "Permission denied" : "Authentication required",
            Detail = detail,
        };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
