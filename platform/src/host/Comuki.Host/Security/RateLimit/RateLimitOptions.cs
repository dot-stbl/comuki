using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Security.RateLimit;

/// <summary>
/// Per-endpoint rate-limit budgets (issue #10 T11.4 security pass).
/// Bound from <c>Host:RateLimit</c>. The host installs one
/// <see cref="System.Threading.RateLimiting.RateLimiter"/> per named
/// partition (<c>login</c>, <c>oidc-start</c>, <c>run-decide</c>,
/// <c>api</c>) and tags endpoints via <c>EnableRateLimiting("…")</c>.
/// <para>
/// All values are windows per minute (the host partitions the limiter
/// at one minute granularity). A <c>0</c> disables the named limiter;
/// the named partition is then a no-op and the request falls through
/// to the unrated path. This is the documented escape hatch for ops
/// to temporarily lift a budget under attack-investigation pressure
/// without redeploying.
/// </para>
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Host:RateLimit";

    /// <summary>Permits per minute on <c>POST /api/v1/auth/login</c> — defeats password spraying.</summary>
    [Range(0, 10_000)]
    public int LoginPermitsPerMinute { get; init; } = 10;

    /// <summary>Permits per minute on <c>GET /api/v1/auth/oidc/{provider}/start</c>.</summary>
    [Range(0, 10_000)]
    public int OidcStartPermitsPerMinute { get; init; } = 30;

    /// <summary>Permits per minute on <c>POST /api/v1/runs/{id}/approve</c> and <c>/cancel</c>.</summary>
    [Range(0, 10_000)]
    public int RunDecisionPermitsPerMinute { get; init; } = 60;

    /// <summary>Generic API budget per subject (authenticated) per minute.</summary>
    [Range(0, 100_000)]
    public int ApiPermitsPerMinute { get; init; } = 600;
}

/// <summary>Named rate-limit partitions registered by the host composition root.</summary>
public static class RateLimitPolicies
{
    /// <summary><c>POST /api/v1/auth/login</c>.</summary>
    public const string Login = "comuki.ratelimit.login";

    /// <summary><c>GET /api/v1/auth/oidc/{provider}/start</c>.</summary>
    public const string OidcStart = "comuki.ratelimit.oidc-start";

    /// <summary><c>POST /api/v1/runs/{id}/approve</c> + <c>/cancel</c>.</summary>
    public const string RunDecision = "comuki.ratelimit.run-decision";

    /// <summary>Generic API (the default fallback when an endpoint opts into rate-limiting).</summary>
    public const string Api = "comuki.ratelimit.api";
}
