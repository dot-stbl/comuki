using Comuki.Modules.Costs.Application.Queries;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Costs;

/// <summary>
/// Thin REST surface for costs (S9 T9.5 + platform rollup):
/// <list type="bullet">
///   <item><c>GET /api/v1/projects/{projectId}/costs</c> — one project's summary, permission <c>cost:read</c>.</item>
///   <item><c>GET /api/v1/costs</c> — platform-wide rollup over a day window (per-project + per-day slices), permission <c>cost:read</c>.</item>
/// </list>
/// </summary>
public static class CostsModuleEndpoints
{
    /// <summary>Maps the costs endpoints.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapCostsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.ProjectCosts, GetCostsAsync).WithTags("Costs");
        app.MapGet(ApiRoutes.Costs, GetPlatformCostsAsync).WithTags("Costs");
        return app;
    }

    [RequiresPermission("cost:read")]
    private static async Task<IResult> GetCostsAsync(
        Guid projectId,
        GetProjectCostsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(new ProjectId(projectId), cancellationToken);
        return Results.Ok(view);
    }

    [RequiresPermission("cost:read")]
    private static async Task<IResult> GetPlatformCostsAsync(
        int? days,
        GetPlatformCostsHandler handler,
        CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(days, cancellationToken);
        return Results.Ok(view);
    }
}
