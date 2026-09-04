namespace Comuki.Host.Security.Cors;

/// <summary>
/// CORS configuration for the versioned API surface. The dashboard SPA
/// is the only browser-origin caller the host expects; the API is also
/// open to API-key bearer clients whose Origin the browser never sets.
/// <para>
/// <see cref="AllowedOrigins"/> is the strict allow-list — anything not
/// present is rejected at the CORS layer (the browser stops the
/// response, no controller code runs). <see cref="AllowWildcard"/> is
/// the escape hatch: <c>true</c> swaps the allow-list for <c>*</c>;
/// refuse both — strict list wins, <c>*</c> requires explicit opt-in
/// and is only honored when the resolved env is NOT <c>Production</c>
/// (issue #10 T11.4 security pass).
/// </para>
/// </summary>
public sealed class ComukiCorsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Host:Cors";

    /// <summary>Allowed browser origins (no scheme / no trailing slash in the value).</summary>
    public string[] AllowedOrigins { get; init; } = ["http://localhost:17173"];

    /// <summary>
    /// When <c>true</c>, the builder emits <c>*</c> instead of the
    /// configured list. <c>Production</c> refuses <c>true</c> and throws
    /// at startup — the wildcard is dev-only.
    /// </summary>
    public bool AllowWildcard { get; init; }

    /// <summary>Headers the browser may send on the preflight request.</summary>
    public string[] AllowedHeaders { get; init; } = ["Authorization", "Content-Type"];

    /// <summary>HTTP methods the browser may call cross-origin.</summary>
    public string[] AllowedMethods { get; init; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];
}
