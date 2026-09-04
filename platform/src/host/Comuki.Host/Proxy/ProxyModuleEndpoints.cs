using Comuki.Modules.Proxy.Application.Options;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Proxy;

/// <summary>
/// Endpoints the proxy surface adds to the host composition:
/// <list type="bullet">
///   <item><c>GET /v1/models</c> — static catalogue the host holds in <see cref="ProxyOptions.KnownModels"/>; the <c>VirtualKey</c> authentication scheme enforces a valid bearer.</item>
///   <item><c>POST /v1/chat/completions</c> and <c>POST /v1/messages</c> — handled by YARP (<see cref="Builder.YarpEndpointExtensions.MapReverseProxy"/>); the <see cref="Modules.Proxy.Infrastructure.Auth.VirtualKeyAuthenticationHandler"/> authenticates and the YARP pipeline rewrites the auth header to the upstream key.</item>
/// </list>
/// </summary>
public static class ProxyModuleEndpoints
{
    /// <summary>Maps <c>/v1/models</c>; the chat / messages routes go through YARP directly.</summary>
    /// <param name="app">Route builder.</param>
    public static IEndpointRouteBuilder MapProxyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.ProxyModels, ListModelsAsync)
            .RequireAuthorization(Modules.Proxy.Infrastructure.Auth.VirtualKeyAuthenticationHandler.SchemeName)
            .WithTags("Provider");
        return app;
    }

    private static IResult ListModelsAsync(IOptions<ProxyOptions> options)
    {
        var snapshot = options.Value;
        var models = snapshot.KnownModels ?? [];
        return Results.Ok(new
        {
            @object = "list",
            data = models.Select(static model => new
            {
                id = model,
                @object = "model",
                created = 0L,
                owned_by = "comuki-proxy",
            }).ToList(),
        });
    }
}
