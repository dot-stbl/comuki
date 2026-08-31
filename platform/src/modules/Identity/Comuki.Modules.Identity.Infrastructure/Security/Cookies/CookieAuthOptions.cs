using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Identity.Infrastructure.Security.Cookies;

/// <summary>
/// Cookie session settings, bound from <c>auth:cookie</c>. Sliding
/// expiry by default — "stay logged in while active", with the
/// security-stamp validation (<c>tokens_version</c>) as the kill switch.
/// </summary>
public sealed class CookieAuthOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "auth:cookie";

    /// <summary>Cookie name when configuration does not say otherwise.</summary>
    public const string DefaultCookieName = "comuki.auth";

    /// <summary>The session cookie name.</summary>
    public string Name { get; init; } = DefaultCookieName;

    /// <summary>How long a login cookie lives.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan ExpireTimeSpan { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Whether activity extends the cookie lifetime.</summary>
    public bool SlidingExpiration { get; init; } = true;
}
