using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// Startup worker that idempotently ensures the configured MinIO bucket
/// exists when <see cref="ArtifactsOptions.AutoCreateBucket"/> is on,
/// behind the comuki worker registry. The compose <c>minio-init</c> job
/// (deploy/docker-compose.yml) is the operator-facing path; the in-process
/// flag is a convenience for greenfield setups (e.g. the integration test)
/// and stays off in production where bucket provisioning is an operator
/// concern. <see cref="MinioRunArtifactStore.EnsureBucketAsync"/> tolerates
/// the "already exists" race so a second instance booting at the same time
/// is a no-op.
/// </summary>
/// <param name="store">The artifact store — exposes the bucket probe.</param>
/// <param name="options">Bound artifact options — reads <see cref="ArtifactsOptions.AutoCreateBucket"/>.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ArtifactBucketInitializerComukiWorker(
    MinioRunArtifactStore store,
    IOptions<ArtifactsOptions> options,
    ILogger<ArtifactBucketInitializerComukiWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "bucket-init";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Startup();

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.AutoCreateBucket)
        {
            return WorkerResult.Ok("auto-create off");
        }

        try
        {
            await store.EnsureBucketAsync(cancellationToken);
            logger.LogInformation(
                "Ensured artifact bucket {Bucket} (AutoCreateBucket=true)",
                options.Value.Bucket);

            return WorkerResult.Ok($"ensured bucket {options.Value.Bucket}");
        }
        catch (Exception exception)
        {
            // Warn rather than fail — the build-time OpenAPI extractor
            // boots the host without a real MinIO and must not report the
            // worker unhealthy. In a real deployment the first List/Upload
            // will surface a clear BucketNotFoundException if the bucket is
            // still missing.
            logger.LogWarning(
                exception,
                "Could not ensure artifact bucket {Bucket} at startup; will retry on first use",
                options.Value.Bucket);

            return WorkerResult.Ok($"bucket {options.Value.Bucket} not ensured; will retry on first use");
        }
    }
}
