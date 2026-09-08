using System.Net.Http.Headers;
using System.Text.Json;
using Comuki.Modules.Proxy.Application.Budgeting;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Modules.Proxy.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Transforms;

namespace Comuki.Modules.Proxy.Infrastructure.Yarp;

/// <summary>
/// Pluggable request / response transforms the proxy registers on every
/// YARP route. The request transform reads the virtual-key auth state the
/// <see cref="VirtualKeyAuthenticationHandler"/> set, enforces the
/// per-request guards (<see cref="ProxyRequestGuards"/> — budget + input
/// + output token caps) and rewrites the outbound <c>Authorization</c>
/// header to the upstream API key the configured virtual key references;
/// the response transform meters the metered upstream usage through the
/// costs module.
/// </summary>
public static class ProxyTransforms
{
    /// <summary>Request transform — guards + swap virtual-key bearer for the upstream key.</summary>
    /// <param name="context">YARP request transform context.</param>
    public static async ValueTask RewriteAuthFromVirtualKeyAsync(RequestTransformContext context)
    {
        var httpContext = context.HttpContext;
        var token = httpContext.User.FindFirst(ProxyClaimNames.VirtualKey)?.Value;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var services = httpContext.RequestServices;
        var store = services.GetRequiredService<IVirtualKeyStore>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Comuki.Proxy.Transforms");

        var key = await store.FindAsync(token, context.CancellationToken);
        if (key is null)
        {
            logger.LogWarning(
                "Virtual key {TokenPrefix} resolved at auth time is no longer present in the store",
                token[..Math.Min(8, token.Length)]);
            return;
        }

        var contentLength = httpContext.Request.ContentLength ?? 0L;
        var requestedMaxOutput = await TryReadMaxOutputTokensAsync(httpContext.Request, context.CancellationToken);

        var enforcer = services.GetRequiredService<IProxyBudgetEnforcer>();
        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key,
            enforcer,
            contentLength,
            requestedMaxOutput,
            context.CancellationToken);
        if (rejection is not null)
        {
            await ProxyRejectionResponseWriter.WriteAsync(
                httpContext, rejection, logger, context.CancellationToken);
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
        // YARP's TransformResponseAsync contract: implementations must NOT
        // consume the response body — YARP streams it to the client after
        // the transform completes. The body-based usage extractor family
        // (OpenAI / Anthropic) therefore stays dormant in v1; metering
        // resumes once the suite adopts a streaming-aware reader or a
        // separate buffered-response middleware. The `context` parameter is
        // reserved for that follow-up (interface contract, can't drop).
        await Task.CompletedTask;
    }

    /// <summary>
    /// Peek the request body for a <c>max_tokens</c> / <c>max_output_tokens</c>
    /// field. The body must be JSON; anything else (including content that
    /// overflows the safe-read cap) returns <c>null</c> so the caller
    /// treats the cap as not exercised. Uses
    /// <see cref="HttpRequest.EnableBuffering"/> so the transform does
    /// not consume the stream YARP needs to forward.
    /// </summary>
    /// <param name="request">Inbound request — body may be rewound.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<int?> TryReadMaxOutputTokensAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is null or > MaxBodyPeekBytes)
        {
            return null;
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var field in maxTokensFieldNames)
            {
                if (document.RootElement.TryGetProperty(field, out var node)
                    && node.ValueKind == JsonValueKind.Number
                    && node.TryGetInt32(out var value))
                {
                    return value;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    /// <summary>Hard ceiling on the body peek for the output-token guard — protects against an upstream-style large body.</summary>
    private const int MaxBodyPeekBytes = 256 * 1024;

    /// <summary>Field names the OpenAI / Anthropic / OpenAI-compatible APIs accept.</summary>
    private static readonly string[] maxTokensFieldNames = ["max_tokens", "max_output_tokens"];
}

/// <summary>
/// Writes a <see cref="ProxyRequestGuards.GuardRejection"/> to the
/// inbound <see cref="HttpContext"/> as a <c>application/problem+json</c>
/// body. Once <c>Response.HasStarted</c> is true YARP's proxy pipeline
/// short-circuits — the upstream call never fires. Extracted to a
/// sibling file so <see cref="ProxyTransforms"/> stays free of private
/// helpers (<c>class-layout-and-tooling.md</c> §1a).
/// </summary>
internal static class ProxyRejectionResponseWriter
{
    /// <summary>Writes the rejection body and commits the response.</summary>
    /// <param name="httpContext">Inbound request's HTTP context.</param>
    /// <param name="rejection">Guard rejection to surface.</param>
    /// <param name="logger">Structured log sink — carries the rejection code + status.</param>
    /// <param name="cancellationToken"></param>
    public static async Task WriteAsync(
        HttpContext httpContext,
        ProxyRequestGuards.GuardRejection rejection,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Proxy request {RequestMethod} {RequestPath} rejected with {StatusCode} {Code}: {Detail}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            rejection.StatusCode,
            rejection.Code,
            rejection.Detail);

        httpContext.Response.StatusCode = rejection.StatusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = $"urn:comuki:error:{rejection.Code}",
            title = rejection.Code switch
            {
                ProxyRequestGuards.BudgetExceededCode => "Budget exceeded",
                ProxyRequestGuards.InputTokensExceededCode => "Input token cap exceeded",
                ProxyRequestGuards.OutputTokensExceededCode => "Output token cap exceeded",
                _ => "Request rejected",
            },
            status = rejection.StatusCode,
            detail = rejection.Detail,
            code = rejection.Code,
        };

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, JsonSerializerOptions.Web, cancellationToken);
    }
}
