using Comuki.Host.Realtime.Reading;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Infrastructure.Security;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Comuki.Host.Realtime;

/// <summary>
/// The realtime surface (issue #7): dashboard clients join the timeline
/// group of one run or the attention group of one project. Hub methods do
/// not pass through the MVC permission filter, so each join resolves the
/// RBAC subject from the connection principal and checks the key itself:
/// <c>run:read</c> on the run's project for run groups, <c>project:read</c>
/// for project attention groups (any project-scoped read — the honest "can
/// see this project at all" key). Anonymous connections never reach a
/// method: <see cref="AuthorizeAttribute"/> fails the handshake.
/// </summary>
/// <param name="evaluator">Permission evaluator (action + object axis input).</param>
/// <param name="runProjects">Run → project lookup for the object axis.</param>
[Authorize]
public sealed class RunsHub(
    IPermissionEvaluator evaluator,
    IRealtimeRunProjects runProjects) : Hub
{
    /// <summary>
    /// Joins the <c>run:{id}</c> timeline group. Requires <c>run:read</c> on
    /// the run's project; an unknown run is an error to the caller.
    /// Cancellation rides <see cref="HubCallerContext.ConnectionAborted"/> —
    /// hub methods must not take a <see cref="CancellationToken"/> parameter
    /// or clients serialise it as an invocation argument.
    /// </summary>
    /// <param name="runId"></param>
    public async Task JoinRunAsync(Guid runId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var id = new RunId(runId);
        var projects = await runProjects.ReadAsync([id], cancellationToken);

        if (!projects.TryGetValue(id, out var project))
        {
            throw new HubException(JoinErrors.RunNotFound);
        }

        await RunsHubPermissions.RequireAsync(evaluator, Context.User, Permissions.RunRead, project, cancellationToken);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.RunGroup(id), cancellationToken);
    }

    /// <summary>Leaves the <c>run:{id}</c> group; leaving is always allowed.</summary>
    /// <param name="runId"></param>
    public Task LeaveRunAsync(Guid runId)
    {
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RealtimeGroups.RunGroup(new RunId(runId)),
            Context.ConnectionAborted);
    }

    /// <summary>
    /// Joins the <c>project:{id}:attention</c> group. Requires
    /// <c>project:read</c> on that project. No existence check: projects
    /// live in the projects module context, and joining an unknown project
    /// at worst subscribes to a group nobody broadcasts into.
    /// </summary>
    /// <param name="projectId"></param>
    public async Task JoinProjectAsync(Guid projectId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var id = new ProjectId(projectId);
        await RunsHubPermissions.RequireAsync(evaluator, Context.User, Permissions.ProjectRead, id, cancellationToken);
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            RealtimeGroups.ProjectAttentionGroup(id),
            cancellationToken);
    }

    /// <summary>Leaves the <c>project:{id}:attention</c> group; leaving is always allowed.</summary>
    /// <param name="projectId"></param>
    public Task LeaveProjectAsync(Guid projectId)
    {
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RealtimeGroups.ProjectAttentionGroup(new ProjectId(projectId)),
            Context.ConnectionAborted);
    }
}

/// <summary>Stable error codes surfaced to hub callers.</summary>
internal static class JoinErrors
{
    public const string RunNotFound = "run.not_found";

    public const string PermissionDenied = "permission.denied";

    public const string AuthenticationRequired = "authentication.required";
}

/// <summary>
/// Permission gate of the hub joins: resolves the connection principal into
/// an RBAC subject and demands the key for the project. A miss is a
/// <see cref="HubException"/> to the caller — the connection stays, the
/// group is never joined.
/// </summary>
file static class RunsHubPermissions
{
    public static async Task RequireAsync(
        IPermissionEvaluator evaluator,
        System.Security.Claims.ClaimsPrincipal? principal,
        PermissionKey key,
        ProjectId project,
        CancellationToken cancellationToken)
    {
        if (RunsHubPrincipal.Resolve(principal) is not { } subject)
        {
            throw new HubException(JoinErrors.AuthenticationRequired);
        }

        var authorization = await evaluator.EvaluateAsync(subject, cancellationToken);

        if (!authorization.IsPermittedIn(key, project))
        {
            throw new HubException(JoinErrors.PermissionDenied);
        }
    }
}

/// <summary>
/// Principal → <see cref="RoleSubject"/> for the hub: the api-key claim
/// resolves to the key subject, otherwise the nameidentifier claim resolves
/// to the user subject. Mirrors the resolver the enforcement filter and the
/// scope middleware use; kept local because those copies are file-private
/// by design.
/// </summary>
file static class RunsHubPrincipal
{
    public static RoleSubject? Resolve(System.Security.Claims.ClaimsPrincipal? principal)
    {
        return principal is null
            ? null
            : OfClaim(IdentityClaimNames.ApiKeyId, SubjectType.ApiKey, principal)
                ?? OfClaim(System.Security.Claims.ClaimTypes.NameIdentifier, SubjectType.User, principal);
    }

    public static RoleSubject? OfClaim(string claimName, SubjectType type, System.Security.Claims.ClaimsPrincipal principal)
    {
        return principal.FindFirst(claimName)?.Value is { Length: > 0 } value
            && Guid.TryParse(value, out var id)
            ? new RoleSubject(type, id)
            : null;
    }
}
