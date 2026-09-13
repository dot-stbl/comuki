using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Read;

/// <summary>
/// Dashboard read surface for workers — the twin of the worker-runtime
/// REST surface in <see cref="Api.WorkerEndpoints"/>. The registry is
/// derived from live work-item leases plus the journal (see
/// <see cref="WorkersReadHandler"/>); drain and force-stop answer
/// <c>501</c> honestly because this host composes neither a scale
/// supervisor drain flag nor a compute provider to stop.
/// </summary>
public static class WorkersReadEndpoints
{
    /// <summary>Maps the dashboard workers endpoints onto the app.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapWorkersReadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Workers, ListAsync).WithTags("Workers");
        app.MapGet(ApiRoutes.WorkerById, GetAsync).WithTags("Workers");
        app.MapPost(ApiRoutes.WorkerDrain, DrainAsync).WithTags("Workers");
        app.MapPost(ApiRoutes.WorkerStop, StopAsync).WithTags("Workers");
        return app;
    }

    [RequiresPermission("queue:read")]
    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        WorkersReadHandler handler,
        CancellationToken cancellationToken)
    {
        var workers = await handler.ListAsync(page ?? 1, pageSize ?? 25, cancellationToken);
        return Results.Ok(workers);
    }

    [RequiresPermission("queue:read")]
    private static async Task<IResult> GetAsync(
        Guid workerId,
        WorkersReadHandler handler,
        CancellationToken cancellationToken)
    {
        return await handler.GetAsync(new WorkerId(workerId), cancellationToken) is { } worker
            ? Results.Ok(worker)
            : WorkersReadResults.WorkerNotFound(workerId);
    }

    [RequiresPermission("run:stop")]
    private static IResult DrainAsync(Guid workerId)
    {
        return WorkersReadResults.DrainUnsupported(workerId);
    }

    [RequiresPermission("run:stop")]
    private static IResult StopAsync(Guid workerId)
    {
        return WorkersReadResults.StopUnsupported(workerId);
    }
}

/// <summary>Problem results of the dashboard workers surface (same shape as the runs surface).</summary>
internal static class WorkersReadResults
{
    /// <summary>404 — no live lease and no recent claim names the worker.</summary>
    public static IResult WorkerNotFound(Guid workerId)
    {
        return TypedResults.Problem(
            title: "Worker not found",
            detail: $"worker '{workerId}' has no live lease and no recent claim",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "worker.not_found",
                ["workerId"] = workerId.ToString(),
            });
    }

    /// <summary>
    /// 501 — draining needs a per-worker "stop claiming" flag on the claim
    /// loop / scale supervisor; neither exists in this host.
    /// </summary>
    public static IResult DrainUnsupported(Guid workerId)
    {
        return TypedResults.Problem(
            title: "Worker drain not implemented",
            detail: "the claim loop has no per-worker drain flag; a worker keeps claiming until its runtime stops",
            statusCode: StatusCodes.Status501NotImplemented,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "worker.drain_unsupported",
                ["workerId"] = workerId.ToString(),
            });
    }

    /// <summary>
    /// 501 — stopping needs an <c>IComputeProvider</c> handle for the
    /// worker's container; this host does not compose the compute engine,
    /// so there is no runtime to tear down through the API.
    /// </summary>
    public static IResult StopUnsupported(Guid workerId)
    {
        return TypedResults.Problem(
            title: "Worker stop not implemented",
            detail: "this host composes no compute provider, so a worker runtime cannot be stopped through the API; stop the container at its provider",
            statusCode: StatusCodes.Status501NotImplemented,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "worker.stop_unsupported",
                ["workerId"] = workerId.ToString(),
            });
    }
}
