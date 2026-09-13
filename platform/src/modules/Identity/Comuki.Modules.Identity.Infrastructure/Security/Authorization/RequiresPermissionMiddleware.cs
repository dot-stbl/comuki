using Comuki.Modules.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;

namespace Comuki.Modules.Identity.Infrastructure.Security.Authorization;

/// <summary>
/// The action axis of the authorization model for non-MVC endpoints
/// (minimal APIs): reads the <see cref="Application.Permissions.RequiresPermissionAttribute"/>
/// demand off the endpoint metadata (handler-method attributes land there
/// since net7) and runs it through the shared <see cref="PermissionGate"/>.
/// MVC actions keep <see cref="RequiresPermissionFilter"/> — both paths
/// answer the same 401 / 403 problem shapes. Endpoints without a demand
/// (health, login, the worker runtime) pass straight through.
/// </summary>
/// <param name="next"></param>
public sealed class RequiresPermissionMiddleware(RequestDelegate next)
{
    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, IPermissionEvaluator evaluator)
    {
        // Last wins, matching the MVC filter's metadata ordering.
        var demand = context.GetEndpoint()?.Metadata
            .OfType<Application.Permissions.RequiresPermissionAttribute>()
            .LastOrDefault();

        if (demand is not null
            && await PermissionGate.EvaluateAsync(context.User, evaluator, demand.PermissionKey, context.RequestAborted) is { } denial)
        {
            context.Response.StatusCode = denial.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(denial.Problem);
            return;
        }

        await next(context);
    }
}
