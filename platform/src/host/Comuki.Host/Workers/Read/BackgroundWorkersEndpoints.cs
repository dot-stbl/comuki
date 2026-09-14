using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Bootstrap.Workers;

namespace Comuki.Host.Workers.Read;

/// <summary>
/// Read-only status of the comuki worker registry — the background
/// loops this host runs (memory-sweep, lease-reaper, oidc-sweep) with
/// their last/next run and consecutive-failure counts. Sits next to
/// <see cref="WorkersReadEndpoints"/> (live worker containers) under
/// the same permission.
/// </summary>
public static class BackgroundWorkersEndpoints
{
    /// <summary>Maps the background-workers status endpoint onto the app.</summary>
    /// <param name="app">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapBackgroundWorkersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.WorkersBackground, ListAsync).WithTags("Workers");
        return app;
    }

    [RequiresPermission("queue:read")]
    private static IResult ListAsync(ComukiWorkerRegistry registry)
    {
        return Results.Ok(registry.Snapshot());
    }
}
