using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Security.Tls;

/// <summary>
/// HTTPS listener configuration for the orchestrator host. Bound from
/// <c>Host:Tls</c> (env: <c>COMUKI_HOST_TLS_*</c>). Disabled by default —
/// the host serves plain HTTP exactly as before. When
/// <see cref="Enabled"/> is set the host keeps the HTTP listener
/// (resolved as usual from <c>[server]</c> port /
/// <c>ASPNETCORE_HTTP_PORTS</c>, default 8080) and adds an HTTPS
/// listener on <see cref="HttpsPort"/> next to it; both stay up so
/// health probes and in-cluster callers keep working while browsers are
/// bounced to HTTPS by the redirect.
/// <para>
/// Certificate: point <see cref="CertificatePath"/> +
/// <see cref="CertificateKeyPath"/> at a PEM pair (e.g. a mounted
/// Kubernetes TLS secret). When both are unset Kestrel falls back to
/// its default certificate — the <c>dotnet dev-certs</c> one in
/// Development, a startup failure in a container without one.
/// </para>
/// </summary>
public sealed class HostTlsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Host:Tls";

    /// <summary>Enable the HTTPS listener (and the HTTP→HTTPS redirect unless <see cref="RedirectHttp"/> is false).</summary>
    public bool Enabled { get; init; }

    /// <summary>HTTPS listen port. The HTTP listener keeps its own port — the two are independent.</summary>
    [Range(1, 65535)]
    public int HttpsPort { get; init; } = 8081;

    /// <summary>
    /// Redirect all plain-HTTP traffic to HTTPS. <c>null</c> (unset)
    /// means <c>true</c> when <see cref="Enabled"/> is on; set
    /// <c>false</c> to keep both listeners serving without redirects.
    /// Ignored when <see cref="Enabled"/> is false.
    /// </summary>
    public bool? RedirectHttp { get; init; }

    /// <summary>Path to the PEM certificate file (e.g. <c>/certs/tls.crt</c>). Must be paired with <see cref="CertificateKeyPath"/>.</summary>
    public string? CertificatePath { get; init; }

    /// <summary>Path to the PEM private key file (e.g. <c>/certs/tls.key</c>). Must be paired with <see cref="CertificatePath"/>.</summary>
    public string? CertificateKeyPath { get; init; }

    /// <summary>
    /// Explicitly opt into the ASP.NET Core development certificate
    /// (<c>dotnet dev-certs</c>). Dev-only convenience — equivalent to
    /// leaving both certificate paths unset, but refuses to combine with
    /// them (validated at startup).
    /// </summary>
    public bool? UseDevCertificate { get; init; }
}
