using System.Net.Http.Headers;
using Comuki.Modules.Proxy.Application.Metering;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Modules.Proxy.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Transforms;

namespace Comuki.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Pluggable request / response transforms the proxy registers on every
/// YARP route. The request transform reads the virtual-key auth state the
/// <see cref="VirtualKeyAuthenticationHandler"/> set and rewrites the
/// outbound <c>Authorization</c> header to the upstream API key the
/// configured virtual key references; the response transform meters the
/// metered upstream usage through the costs module.
/// </summary>
public static class ProxyTransforms
{
    /// <summary>Request transform — swap virtual-key bearer for the upstream key.</summary>
    /// <param name="context">YARP request transform context.</param>
    public static async ValueTask RewriteAuthFromVirtualKeyAsync(RequestTransformContext context)
    {
        var httpContext = context.HttpContext;
        var token = httpContext.User.FindFirst(ProxyClaimNames.VirtualKey)?.Value;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var store = httpContext.RequestServices.GetRequiredService<IVirtualKeyStore>();
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Comuki.Proxy.Transforms");

        var key = await store.FindAsync(token, context.CancellationToken);
        if (key is null)
        {
            logger.LogWarning(
                "Virtual key {TokenPrefix} resolved at auth time is no longer present in the store",
                token[..Math.Min(8, token.Length)]);
            return;
        }

        var upstreamApiKey = Environment.GetEnvironmentVariable(key.Upstream.ApiKeyEnvRef);
        if (string.IsNullOrEmpty(upstreamApiKey))
        {
            logger.LogError(
                "Upstream API key env var {EnvRef} is unset for virtual key on project {ProjectId}; downstream call will fail",
                key.Upstream.ApiKeyEnvRef,
                key.ProjectId);
            return;
        }

        context.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", upstreamApiKey);
        context.ProxyRequest.Headers.Host = null;
    }

    /// <summary>Response transform — meter upstream usage into the costs module.</summary>
    /// <param name="context">YARP response transform context.</param>
    public static async ValueTask MeterUsageFromResponseAsync(ResponseTransformContext context)
    {
        var httpContext = context.HttpContext;
        var proxyResponse = context.ProxyResponse;
        if (proxyResponse is null)
        {
            return;
        }

        var status = (int)proxyResponse.StatusCode;
        if (status is < 200 or >= 300)
        {
            return;
        }

        var mediaType = proxyResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var body = await proxyResponse.Content.ReadAsStringAsync(context.CancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var token = httpContext.User.FindFirst(ProxyClaimNames.VirtualKey)?.Value;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var store = httpContext.RequestServices.GetRequiredService<IVirtualKeyStore>();
        var key = await store.FindAsync(token, context.CancellationToken);
        if (key is null)
        {
            return;
        }

        var meter = httpContext.RequestServices.GetRequiredService<ProxyUsageMeter>();
        await meter.MeterAsync(
            provider: key.Upstream.Provider,
            body: body,
            key: key,
            occurredAt: DateTimeOffset.UtcNow,
            cancellationToken: context.CancellationToken);
    }
}
