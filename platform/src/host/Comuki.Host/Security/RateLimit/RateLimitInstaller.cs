using System.Security.Claims;
using System.Threading.RateLimiting;
using Comuki.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace Comuki.Host.Security.RateLimit;

/// <summary>
/// Wires the <see cref="RateLimitOptions"/> and registers the
/// per-partition <see cref="RateLimiter"/>
/// the host composition root attaches to the named
/// <see cref="RateLimitPolicies"/> partitions. Endpoints opt in with
/// <c>[EnableRateLimiting("comuki.ratelimit.login")]</c> on the action.
/// <para>
/// All limiters are fixed-window, one minute, with the permit count
/// from <see cref="RateLimitOptions"/>; the partition key is the caller's
/// <c>sub</c> / api-key id when authenticated, falling back to the
/// remote IP. A zero permit count makes the named partition a no-op
/// (the limiter accepts every request) — the documented escape hatch.
/// </para>
/// </summary>
public static class RateLimitInstaller
{
    /// <summary>Reads <see cref="RateLimitOptions"/> and installs the per-partition limiters.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">Configuration root — binds <see cref="RateLimitOptions"/> from <c>Host:RateLimit</c>.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddComukiRateLimit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddPartition(limiter, RateLimitPolicies.Login, options.LoginPermitsPerMinute);
            AddPartition(limiter, RateLimitPolicies.OidcStart, options.OidcStartPermitsPerMinute);
            AddPartition(limiter, RateLimitPolicies.RunDecision, options.RunDecisionPermitsPerMinute);
            AddPartition(limiter, RateLimitPolicies.Api, options.ApiPermitsPerMinute);
        });

        return services;
    }

    /// <summary>
    /// Adds one fixed-window <see cref="PartitionedRateLimiter{TResource}"/>
    /// partition keyed by subject (authenticated identity or api-key id)
    /// with a client-IP fallback. A <paramref name="permitsPerMinute"/>
    /// of <c>0</c> produces a limiter that accepts every request — the
    /// named partition becomes a no-op without rewriting the endpoint
    /// attributes.
    /// </summary>
    /// <param name="limiter">The host's <see cref="RateLimiterOptions"/>.</param>
    /// <param name="policyName">The named partition / policy identifier.</param>
    /// <param name="permitsPerMinute">Permits per one-minute window; <c>0</c> disables the partition.</param>
    private static void AddPartition(RateLimiterOptions limiter, string policyName, int permitsPerMinute)
    {
        if (permitsPerMinute <= 0)
        {
            return;
        }

        limiter.AddPolicy(policyName, httpContext =>
        {
            var partitionKey = ResolvePartitionKey(httpContext);
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
        });
    }

    /// <summary>
    /// Resolves the partition key for one request: the authenticated
    /// subject id (NameIdentifier / api-key claim) when present, the
    /// remote IP otherwise. The fallback prevents an unauthenticated
    /// burst from pinning a single shared bucket.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        var nameIdentifier = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(nameIdentifier))
        {
            return $"sub:{nameIdentifier}";
        }

        var apiKey = httpContext.User?.FindFirst(IdentityClaimNames.ApiKeyId)?.Value;
        return !string.IsNullOrEmpty(apiKey) ? $"key:{apiKey}" : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
