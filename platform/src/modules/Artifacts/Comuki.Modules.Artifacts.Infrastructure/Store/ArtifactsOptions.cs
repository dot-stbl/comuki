using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// One section of the host configuration the Artifacts module binds from
/// (<c>Artifacts:</c> in appsettings / env). Endpoint / credentials are
/// required — the bootstrap fails loudly when any are missing rather than
/// silently using stale defaults. <see cref="Bucket"/> is the single
/// bucket every artifact object lands in; the project/run prefix on each
/// object key is what scopes visibility.
/// </summary>
public sealed class ArtifactsOptions
{
    /// <summary>Configuration section name — <c>Artifacts</c>.</summary>
    public const string SectionName = "Artifacts";

    /// <summary>
    /// MinIO S3 endpoint — host or <c>host:port</c>. Examples:
    /// <c>minio:9000</c> (compose), <c>localhost:9000</c> (dev).
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string Endpoint { get; init; }

    /// <summary>MinIO access key.</summary>
    [Required]
    [MinLength(1)]
    public required string AccessKey { get; init; }

    /// <summary>MinIO secret key.</summary>
    [Required]
    [MinLength(1)]
    public required string SecretKey { get; init; }

    /// <summary>Bucket name — every artifact object lands here, prefixed by project/run.</summary>
    [Required]
    [MinLength(1)]
    public required string Bucket { get; init; }

    /// <summary>
    /// When true the client speaks HTTPS to MinIO. Dev compose is plain
    /// HTTP; production deployments behind a TLS terminator should leave
    /// this on (default true).
    /// </summary>
    public bool UseSSL { get; init; } = true;

    /// <summary>
    /// Auto-create the configured bucket on first use when it does not
    /// yet exist. The compose <c>minio-init</c> job also creates the
    /// bucket — the host-side flag is a convenience for greenfield setups
    /// (e.g. the integration test) and stays off in production where bucket
    /// provisioning is an operator concern.
    /// </summary>
    public bool AutoCreateBucket { get; init; }
}
