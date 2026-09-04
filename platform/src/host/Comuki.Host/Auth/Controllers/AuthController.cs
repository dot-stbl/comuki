using System.Security.Claims;
using Comuki.Host.Auth.Models;
using Comuki.Host.Security.RateLimit;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Infrastructure.Oidc;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Modules.Identity.Infrastructure.Security.Authorization;
using Comuki.Modules.Identity.Infrastructure.Security.Cookies;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Auth.Controllers;

/// <summary>
/// The authentication surface: local login/logout, the current-subject
/// read for SPA bootstrap, and the OIDC redirect handoff. Login demands
/// no permission (it is how a caller acquires permissions); <c>me</c>
/// reports what the caller holds, so it also demands none — an
/// unauthenticated caller gets 401, an empty-handed one sees an empty
/// answer.
/// </summary>
/// <param name="authentication"></param>
/// <param name="loginValidator"></param>
/// <param name="userStore"></param>
/// <param name="permissionEvaluator"></param>
/// <param name="assignments"></param>
/// <param name="oidc"></param>
/// <param name="logger"></param>
[ApiController]
[Route(ApiRoutes.AuthRoot)]
public sealed class AuthController(
    IUserAuthenticationService authentication,
    IValidator<LoginCommand> loginValidator,
    IUserAccountStore userStore,
    IPermissionEvaluator permissionEvaluator,
    IRoleAssignmentStore assignments,
    IOptions<OidcOptions> oidc,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Stable failure code of a rejected login — no reason detail that would enumerate accounts.</summary>
    public const string InvalidCredentialsCode = "auth.invalid_credentials";

    /// <summary>
    /// Email+password sign-in; on success the cookie session is set by
    /// <see cref="IUserAuthenticationService"/>. Any credential failure
    /// answers the same 401 problem — unknown user and wrong password
    /// read identically.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var validation = await loginValidator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            var typed = TypedResults.ValidationProblem(validation.ToDictionary());
            return new ObjectResult(typed.ProblemDetails)
            {
                StatusCode = typed.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
        }

        var result = await authentication.LoginAsync(command, cancellationToken);

        if (result is not { Success: true, UserId: { } userId })
        {
            logger.LogInformation("Login rejected for {Email}: {FailureCode}", request.Email, result.FailureCode);
            return AuthProblems.Problem(
                StatusCodes.Status401Unauthorized,
                InvalidCredentialsCode,
                "email or password is incorrect");
        }

        var account = await userStore.FindByIdAsync(userId, cancellationToken);

        return account is { } user
            ? Ok(new LoginResponse(user.Id.Value, user.Email, user.DisplayName))
            : AuthProblems.Problem(
                StatusCodes.Status401Unauthorized,
                InvalidCredentialsCode,
                "email or password is incorrect");
    }

    /// <summary>Clears the cookie session.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        await authentication.LogoutAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// The current subject: identity, active roles and effective
    /// permissions. An API-key request reports the key's subject and
    /// the key's assignments, not its owner's.
    /// </summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("me")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeResponse>> MeAsync(CancellationToken cancellationToken = default)
    {
        if (HostSubjects.Resolve(User) is not { } subject)
        {
            return AuthProblems.Problem(
                StatusCodes.Status401Unauthorized,
                RequiresPermissionFilter.AuthenticationRequiredCode,
                "an authenticated subject is required");
        }

        var authorization = await permissionEvaluator.EvaluateAsync(subject, cancellationToken);
        var active = await assignments.ListActiveAsync(subject, cancellationToken);
        var ownerUserId = HostSubjects.OwnerUserIdOf(User);

        return Ok(new MeResponse
        {
            UserId = ownerUserId,
            SubjectType = SubjectTypeKeys.Key(subject.Type),
            SubjectId = subject.Id,
            Email = User.FindFirst(ClaimTypes.Email)?.Value,
            DisplayName = User.FindFirst(ClaimTypes.Name)?.Value,
            Roles = [.. active.Select(static assignment => RoleKeys.Key(assignment.Role)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            Permissions = MeResponse.PermissionsView.From(authorization),
        });
    }

    /// <summary>
    /// Starts the OIDC redirect flow for a configured provider: answers
    /// a challenge against the provider's scheme. Unknown providers are
    /// 404 before any redirect happens.
    /// </summary>
    /// <param name="provider"></param>
    [HttpGet("oidc/{provider}/start")]
    [EnableRateLimiting(RateLimitPolicies.OidcStart)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult StartOidc(string provider)
    {
        return oidc.Value.Providers.Any(configured => configured.Name == provider)
            ? Challenge(new AuthenticationProperties { RedirectUri = "/" }, AuthSchemes.Oidc(provider))
            : AuthProblems.Problem(
                StatusCodes.Status404NotFound,
                "auth.oidc_provider_not_found",
                $"oidc provider '{provider}' is not configured");
    }
}

/// <summary>
/// Principal → <see cref="RoleSubject"/> for the host surface: the
/// api-key claim resolves to the key subject, otherwise the
/// nameidentifier claim resolves to the user subject. Mirrors the
/// resolver the Identity enforcement filter uses; kept local because
/// the module's copy is file-private by design.
/// </summary>
file static class HostSubjects
{
    public static RoleSubject? Resolve(ClaimsPrincipal principal)
    {
        return OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
            ?? OfClaim(ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    public static Guid? OwnerUserIdOf(ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var userId)
            ? userId
            : null;
    }

    public static RoleSubject? OfClaim(string claimName, SubjectType type, ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }
}

/// <summary>Builds the 401/404 problem results the auth surface returns.</summary>
file static class AuthProblems
{
    public static ActionResult Problem(int statusCode, string code, string detail)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
        var typed = TypedResults.Problem(
            title: statusCode == StatusCodes.Status404NotFound ? "Not found" : "Authentication required",
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
