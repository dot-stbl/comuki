using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// Startup hook that idempotently ensures the configured MinIO bucket
/// exists when <see cref="ArtifactsOptions.AutoCreateBucket"/> is on.
/// The compose <c>minio-init</c> job (deploy/docker-compose.yml) is the
/// operator-facing path; the in-process flag is a convenience for
/// greenfield setups (e.g. the integration test) and stays off in
/// production where bucket provisioning is an operator concern.
/// <see cref="MinioRunArtifactStore.EnsureBucketAsync"/> tolerates the
/// "already exists" race so a second instance booting at the same time
/// is a no-op.
/// </summary>
/// <param name="store">The artifact store — exposes the bucket probe.</param>
/// <param name="options">Bound artifact options — reads <see cref="ArtifactsOptions.AutoCreateBucket"/>.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ArtifactBucketInitializer(
    MinioRunArtifactStore store,
    IOptions<ArtifactsOptions> options,
    ILogger<ArtifactBucketInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoCreateBucket)
        {
            return;
        }

        try
        {
            await store.EnsureBucketAsync(cancellationToken);
            logger.LogInformation(
                "Ensured artifact bucket {Bucket} (AutoCreateBucket=true)",
                options.Value.Bucket);
        }
        catch (Exception exception)
        {
            // Warn rather than throw — the build-time OpenAPI extractor
            // boots the host without a real MinIO and must not fail the
            // build. In a real deployment the first List/Upload will
            // surface a clear BucketNotFoundException if the bucket is
            // still missing.
            logger.LogWarning(
                exception,
                "Could not ensure artifact bucket {Bucket} at startup; will retry on first use",
                options.Value.Bucket);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
