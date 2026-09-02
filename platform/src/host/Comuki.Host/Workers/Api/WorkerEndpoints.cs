using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Contracts.Queue;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Workers.Api;

/// <summary>
/// The worker-facing REST surface (T3.3 claim loop): claim an item, extend
/// its lease, complete or fail it. Every endpoint authenticates the worker
/// token from the Authorization header — the worker id the queue sees is
/// the one the token was issued for. Ownership misses (expired lease, wrong
/// owner) are 409 ProblemDetails, not errors.
/// </summary>
public static class WorkerEndpoints
{
    /// <summary>Maps the worker REST endpoints onto the app.</summary>
    /// <param name="app"></param>
    public static void MapWorkerEndpoints(WebApplication app)
    {
        app.MapPost(ApiRoutes.WorkerClaim, ClaimAsync);
        app.MapPost(ApiRoutes.WorkerHeartbeat, HeartbeatAsync);
        app.MapPost(ApiRoutes.WorkerComplete, CompleteAsync);
        app.MapPost(ApiRoutes.WorkerFail, FailAsync);
    }

    private static async Task<IResult> ClaimAsync(
        ClaimWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        ClaimWorkItemHandler claimHandler,
        CancellationToken cancellationToken)
    {
        if (AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return Unauthenticated();
        }

        // The claim loop is a platform-system consumer: it claims across
        // every project, so the subject-scope query filters must not
        // confine it.
        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        var command = new ClaimWorkItemCommand(
            workerId,
            new WorkItemLabels(request.Image, request.ProfilesRef, request.ProfileKey));
        try
        {
            var claimed = await claimHandler.HandleAsync(command, cancellationToken);
            return claimed is null
                ? Results.NoContent()
                : Results.Ok(new ClaimedWorkItemResponse(
                    claimed.WorkItemId,
                    claimed.RunId.Value,
                    claimed.ProfileKey,
                    claimed.Brief,
                    claimed.LeaseUntil.ToUnixTimeMilliseconds(),
                    claimed.Attempt));
        }
        catch (ValidationException exception)
        {
            return TypedResults.ValidationProblem(
                exception.Errors.ToDictionary(
                    static error => error.PropertyName,
                    static error => new[] { error.ErrorMessage }));
        }
    }

    private static async Task<IResult> HeartbeatAsync(
        Guid workItemId,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        TimeProvider clock,
        IOptions<LeaseOptions> leaseOptions,
        CancellationToken cancellationToken)
    {
        if (AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        var now = clock.GetUtcNow();
        var extended = await queue.HeartbeatAsync(
            workItemId, workerId, now.Add(leaseOptions.Value.LeaseTtl), now, cancellationToken);
        return extended ? Results.NoContent() : NotOwner();
    }

    private static async Task<IResult> CompleteAsync(
        Guid workItemId,
        CompleteWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        var completed = await queue.CompleteAsync(workItemId, workerId, request.ResultJson, clock.GetUtcNow(), cancellationToken);
        return completed ? Results.NoContent() : NotOwner();
    }

    private static async Task<IResult> FailAsync(
        Guid workItemId,
        FailWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        var failed = await queue.FailAsync(workItemId, workerId, request.Reason, clock.GetUtcNow(), cancellationToken);
        return failed ? Results.NoContent() : NotOwner();
    }

    private static WorkerId? AuthenticateWorker(WorkerTokenAuthenticator authenticator, HttpContext httpContext)
    {
        return authenticator.Authenticate(WorkerTokenHeaders.TryGetFromHttp(httpContext.Request.Headers));
    }

    private static IResult Unauthenticated()
    {
        return TypedResults.Problem(
            title: "Worker authentication failed",
            detail: "present a valid worker token in the Authorization header",
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = "worker.unauthenticated" });
    }

    private static IResult NotOwner()
    {
        return TypedResults.Problem(
            title: "Work item not owned",
            detail: "the item is unknown, not running, or not leased to this worker (the lease may have expired)",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = "work-item.not-owner" });
    }
}
