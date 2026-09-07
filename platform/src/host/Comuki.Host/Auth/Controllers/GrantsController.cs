using Comuki.Host.Auth.Models;
using Comuki.Host.Auth.Security;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Assignments.List;
using Comuki.Modules.Identity.Application.Assignments.Revoke;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Auth.Controllers;

/// <summary>
/// Role-grant surface (issues #32, #36, #45): grant a role to a user at
/// platform or project scope, revoke an active grant, and list role
/// assignments paged. Mutations demand <c>identity:write</c>; the new
/// read endpoint demands <c>identity:read</c>. The controller resolves
/// the acting subject from the cookie / api-key principal so the
/// seniority guard runs. Validators are per-action via
/// <c>[FromServices]</c>.
/// </summary>
[ApiController]
[Route("api/v1/grants")]
public sealed class GrantsController(
    GrantRoleHandler grantRole,
    RevokeRoleHandler revokeRole,
    ListGrantsHandler listGrants,
    ILogger<GrantsController> logger) : ControllerBase
{
    /// <summary>Grants a role (issue #32). Permission <c>identity:write</c>.</summary>
    /// <param name="request"></param>
    /// <param name="grantRoleValidator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("identity:write")]
    [ProducesResponseType<Modules.Identity.Application.Views.RoleAssignmentView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Modules.Identity.Application.Views.RoleAssignmentView>> GrantRoleAsync(
        [FromBody] GrantRoleRequest request,
        [FromServices] IValidator<GrantRoleRequest> grantRoleValidator,
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
    [HttpPost("{grantId:guid}/revoke")]
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

    /// <summary>
    /// Lists role assignments (issue #45 / F13 — read side of the identity admin).
    /// Permission <c>identity:read</c>; returns a paged
    /// <c>{ items, total }</c> envelope of <see cref="Modules.Identity.Application.Views.RoleAssignmentView"/>.
    /// Optional <c>subjectKind</c> + <c>subjectId</c> narrow to a single user or key.
    /// </summary>
    [HttpGet]
    [RequiresPermission("identity:read")]
    [ProducesResponseType<IdentityAdminGrantsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IdentityAdminGrantsPage>> ListAsync(
        [FromQuery] ListGrantsQueryRequest query,
        [FromServices] IValidator<ListGrantsQueryRequest> listGrantsValidator,
        CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(listGrantsValidator, query, cancellationToken) is { } problem)
        {
            return problem;
        }

        var subjectKind = SubjectTypeKeys.Parse(query.SubjectKind);

        var (items, total) = await listGrants.HandleAsync(
            new ListGrantsQuery(subjectKind, query.SubjectId, query.Page, query.PageSize),
            cancellationToken);

        return Ok(new IdentityAdminGrantsPage(items, total));
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
