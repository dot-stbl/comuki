using Comuki.Host.Auth.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Application.Users;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Auth.Controllers;

/// <summary>
/// User-administration surface (issues #31, #34, #35): invite a user,
/// toggle the disabled flag, manually link an OIDC identity. Each
/// mutation demands <c>identity:write</c>; the controller resolves the
/// acting subject from the cookie / api-key principal so the seniority
/// guard runs. Validators are per-action via <c>[FromServices]</c> —
/// each endpoint pulls only the rule set it needs.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    InviteUserHandler inviteUser,
    SetUserDisabledHandler setUserDisabled,
    LinkOidcSubjectHandler linkOidc,
    ILogger<UsersController> logger) : ControllerBase
{
    /// <summary>Invites a user (issue #31). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="inviteValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.UserAccountView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.UserAccountView>> InviteUserAsync(
        [FromBody] InviteUserRequest request,
        [FromServices] IValidator<InviteUserRequest> inviteValidator,
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
    /// <param name="setDisabledValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPatch("{userId:guid}")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.UserAccountView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Modules.Identity.Application.Views.UserAccountView>> SetDisabledAsync(
        Guid userId,
        [FromBody] SetUserDisabledRequest request,
        [FromServices] IValidator<SetUserDisabledRequest> setDisabledValidator,
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
    /// <param name="linkOidcValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{userId:guid}/oidc-link")]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.OidcLinkView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.OidcLinkView>> LinkOidcAsync(
        Guid userId,
        [FromBody] LinkOidcRequest request,
        [FromServices] IValidator<LinkOidcRequest> linkOidcValidator,
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
