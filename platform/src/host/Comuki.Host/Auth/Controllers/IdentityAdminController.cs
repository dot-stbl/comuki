using Comuki.Host.Auth.Models;
using Comuki.Host.Auth.Security;
using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.ApiKeys.Revoke;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Assignments.Revoke;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Auth.Controllers;

/// <summary>
/// The identity-admin surface (issues #31-#37): invite a user, grant /
/// revoke a role, issue / revoke an API key, link / unlink an OIDC
/// identity, toggle the disabled flag on a user. Every mutation demands
/// <c>identity:write</c>; the controller resolves the acting subject from
/// the cookie / api-key principal so the seniority guard runs.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class IdentityAdminController(
    InviteUserHandler inviteUser,
    SetUserDisabledHandler setUserDisabled,
    LinkOidcSubjectHandler linkOidc,
    GrantRoleHandler grantRole,
    RevokeRoleHandler revokeRole,
    IssueApiKeyHandler issueApiKey,
    RevokeApiKeyHandler revokeApiKey,
    IValidator<InviteUserRequest> inviteValidator,
    IValidator<SetUserDisabledRequest> setDisabledValidator,
    IValidator<LinkOidcRequest> linkOidcValidator,
    IValidator<GrantRoleRequest> grantRoleValidator,
    IValidator<CreateApiKeyRequest> createApiKeyValidator,
    ILogger<IdentityAdminController> logger) : ControllerBase
{
    /// <summary>Invites a user (issue #31). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("users")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.UserAccountView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.UserAccountView>> InviteUserAsync(
        [FromBody] InviteUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(inviteValidator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var view = await inviteUser.HandleAsync(
            new InviteUserCommand(request.Email, request.DisplayName, request.Password),
            cancellationToken);

        logger.LogInformation("User {Email} invited ({UserId})", view.Email, view.Id);

        return Created($"/api/v1/users/{view.Id.Value}", view);
    }

    /// <summary>Toggles the disabled flag (issue #35). Permission <c>identity:write</c>.</summary>
    /// <param name="userId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPatch("users/{userId:guid}")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.UserAccountView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Modules.Identity.Application.Views.UserAccountView>> SetDisabledAsync(
        Guid userId,
        [FromBody] SetUserDisabledRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(setDisabledValidator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var view = await setUserDisabled.HandleAsync(
            new SetUserDisabledCommand(userId, request.Disabled),
            cancellationToken);

        logger.LogInformation("User {UserId} disabled={Disabled}", view.Id, view.Disabled);

        return Ok(view);
    }

    /// <summary>Manually links an OIDC identity (issue #34). Permission <c>identity:write</c>.</summary>
    /// <param name="userId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("users/{userId:guid}/oidc-link")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.OidcLinkView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.OidcLinkView>> LinkOidcAsync(
        Guid userId,
        [FromBody] LinkOidcRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(linkOidcValidator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var view = await linkOidc.HandleAsync(
            new LinkOidcSubjectCommand(userId, request.Provider, request.SubjectId),
            cancellationToken);

        logger.LogInformation("OIDC link {Provider}/{Subject} bound to user {UserId}", view.Provider, view.Subject, userId);

        return Created($"/api/v1/users/{userId}/oidc-link/{view.Id}", view);
    }

    /// <summary>Grants a role (issue #32). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("grants")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.RoleAssignmentView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.RoleAssignmentView>> GrantRoleAsync(
        [FromBody] GrantRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(grantRoleValidator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var role = RoleKeys.Parse(request.Role)
            ?? throw new InvalidOperationException($"unknown role '{request.Role}'");

        var scope = request.ProjectId is { } projectId
            ? AssignmentScope.ForProject(new ProjectId(projectId))
            : AssignmentScope.Platform();

        var view = await grantRole.HandleAsync(
            new GrantRoleCommand(
                RoleSubject.ForUser(new UserId(request.UserId)),
                role,
                scope,
                ActingAs: HostSubjects.Resolve(User)),
            cancellationToken);

        logger.LogInformation("Role {Role} granted to user {UserId} (scope={Scope})", request.Role, request.UserId, scope.Level);

        return Created($"/api/v1/grants/{view.Id.Value}", view);
    }

    /// <summary>Revokes a role assignment (issue #36). Permission <c>identity:write</c>.</summary>
    /// <param name="grantId"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("grants/{grantId:guid}/revoke")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.RoleAssignmentView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Modules.Identity.Application.Views.RoleAssignmentView>> RevokeGrantAsync(
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        var view = await revokeRole.HandleAsync(
            new RevokeRoleCommand(new RoleAssignmentId(grantId), ActingAs: HostSubjects.Resolve(User)),
            cancellationToken);

        logger.LogInformation("Grant {GrantId} revoked", grantId);

        return Ok(view);
    }

    /// <summary>Issues an API key (issue #33). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("keys")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<IssuedApiKeyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssuedApiKeyResponse>> IssueApiKeyAsync(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(createApiKeyValidator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var credential = await issueApiKey.HandleAsync(
            new IssueApiKeyCommand(new UserId(request.UserId), request.Label),
            cancellationToken);

        logger.LogInformation("API key {Prefix} issued for user {UserId}", credential.Prefix, request.UserId);

        var response = new IssuedApiKeyResponse(
            credential.Id.Value,
            credential.Prefix,
            credential.PlaintextToken);

        return Created($"/api/v1/keys/{credential.Id.Value}", response);
    }

    /// <summary>Revokes an API key (issue #37). Permission <c>identity:write</c>.</summary>
    /// <param name="keyId"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("keys/{keyId:guid}/revoke")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<ApiKeyView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyView>> RevokeApiKeyAsync(
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        var view = await revokeApiKey.HandleAsync(keyId, cancellationToken);

        logger.LogInformation("API key {KeyId} revoked", keyId);

        return Ok(view);
    }

    private static async Task<ActionResult?> ValidateAsync<T>(IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);

        if (!result.IsValid)
        {
            var typed = TypedResults.ValidationProblem(result.ToDictionary());

            return new ObjectResult(typed.ProblemDetails)
            {
                StatusCode = typed.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
        }

        return null;
    }
}

/// <summary>
/// The wire shape of <c>POST /api/v1/keys</c>. The plaintext is shown
/// exactly once — the host keeps the prefix + HMAC and never returns the
/// secret again.
/// </summary>
/// <param name="KeyId">Strong-typed api key id.</param>
/// <param name="Prefix">8-char public lookup prefix.</param>
/// <param name="Secret">Full <c>ck_…</c> token; shown once.</param>
public sealed record IssuedApiKeyResponse(Guid KeyId, string Prefix, string Secret);
