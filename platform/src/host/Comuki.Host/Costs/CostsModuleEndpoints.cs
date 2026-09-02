using Comuki.Modules.Costs.Application.Queries;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Costs;

/// <summary>
/// Thin REST surface for project costs (S9 T9.5):
/// GET /api/v1/projects/{projectId}/costs — permission <c>cost:read</c>
/// enforced by the host-wide permission filter
/// (<see cref="RequiresPermissionAttribute"/>).
/// </summary>
public static class CostsModuleEndpoints
{
    /// <summary>Maps the costs endpoints under projects.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapCostsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiRoutes.Projects).WithTags("Costs");
        group.MapGet("/{projectId:guid}/costs", GetCostsAsync);
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
}
