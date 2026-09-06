using Comuki.Host.Auth.Models;
using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.ApiKeys.Revoke;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Ids;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Auth.Controllers;

/// <summary>
/// API-key surface (issues #33, #37): issue a new API key for a user
/// (plaintext shown once) and revoke an active key. Each mutation
/// demands <c>identity:write</c>. Validators are per-action via
/// <c>[FromServices]</c>.
/// </summary>
[ApiController]
[Route("api/v1/keys")]
public sealed class KeysController(
    IssueApiKeyHandler issueApiKey,
    RevokeApiKeyHandler revokeApiKey,
    ILogger<KeysController> logger) : ControllerBase
{
    /// <summary>Issues an API key (issue #33). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="createApiKeyValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<IssuedApiKeyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssuedApiKeyResponse>> IssueApiKeyAsync(
        [FromBody] CreateApiKeyRequest request,
        [FromServices] IValidator<CreateApiKeyRequest> createApiKeyValidator,
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
    [HttpPost("{keyId:guid}/revoke")]
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
