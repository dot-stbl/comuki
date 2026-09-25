using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Orchestration.Application.Handlers;
using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Contracts.Queue;
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
///
/// The pool bookkeeping rides the same calls: claim marks the worker busy,
/// heartbeat touches its activity clock, complete/fail mark it idle again.
/// Without this the scale supervisor sees every worker as perpetually idle
/// and reaps it at the idle TTL mid-run.
/// </summary>
public static class WorkerEndpoints
{
    /// <summary>Maps the worker REST endpoints onto the app.</summary>
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
        IWorkerPoolState pool,
        VirtualKeys.MintedVirtualKeyService virtualKeys,
        CancellationToken cancellationToken)
    {
        if (WorkerEndpointHelpers.AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return WorkerResults.Unauthenticated();
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
            if (claimed is null)
            {
                return Results.NoContent();
            }

            // Busy from the claim until the terminal complete/fail call —
            // the idle reaper must not collect a worker mid-run.
            pool.MarkBusy(workerId);

            // Mint after the claim transaction: the queue's journal event
            // mirrors the transition only, and the raw token appears
            // exactly once — in this response body.
            var minted = await virtualKeys.MintAsync(
                claimed.ProjectId, claimed.WorkItemId, claimed.LeaseUntil, cancellationToken);
            return Results.Ok(new ClaimedWorkItemResponse(
                claimed.WorkItemId,
                claimed.RunId.Value,
                claimed.ProjectId,
                claimed.ProfileKey,
                claimed.Brief,
                claimed.LeaseUntil.ToUnixTimeMilliseconds(),
                claimed.Attempt,
                claimed.Generation,
                ProxyBaseUrl: minted?.ProxyBaseUrl,
                VirtualKey: minted?.Token));
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
        HeartbeatWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        IWorkerPoolState pool,
        TimeProvider clock,
        IOptions<LeaseOptions> leaseOptions,
        CancellationToken cancellationToken)
    {
        if (WorkerEndpointHelpers.AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return WorkerResults.Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        var now = clock.GetUtcNow();
        var extended = await queue.HeartbeatAsync(
            workItemId, workerId, request.Generation, now.Add(leaseOptions.Value.LeaseTtl), now, cancellationToken);
        if (extended)
        {
            pool.Touch(workerId);
            return Results.NoContent();
        }

        return WorkerResults.NotOwner();
    }

    private static async Task<IResult> CompleteAsync(
        Guid workItemId,
        CompleteWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        IWorkerPoolState pool,
        TimeProvider clock,
        VirtualKeys.MintedVirtualKeyService virtualKeys,
        CancellationToken cancellationToken)
    {
        if (WorkerEndpointHelpers.AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return WorkerResults.Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        await virtualKeys.RevokeAsync(workItemId, cancellationToken);
        var completed = await queue.CompleteAsync(
            workItemId, workerId, request.Generation, request.ResultJson, clock.GetUtcNow(), cancellationToken);
        if (completed)
        {
            pool.MarkIdle(workerId);
            return Results.NoContent();
        }

        return WorkerResults.NotOwner();
    }

    private static async Task<IResult> FailAsync(
        Guid workItemId,
        FailWorkItemRequest request,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        IWorkItemQueue queue,
        IWorkerPoolState pool,
        TimeProvider clock,
        VirtualKeys.MintedVirtualKeyService virtualKeys,
        CancellationToken cancellationToken)
    {
        if (WorkerEndpointHelpers.AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return WorkerResults.Unauthenticated();
        }

        using var systemScope = scopeAccessor.AsSystem("worker-runtime");
        await virtualKeys.RevokeAsync(workItemId, cancellationToken);
        var failed = await queue.FailAsync(
            workItemId, workerId, request.Generation, request.Reason, clock.GetUtcNow(), cancellationToken);
        if (failed)
        {
            pool.MarkIdle(workerId);
            return Results.NoContent();
        }

        return WorkerResults.NotOwner();
    }
}
