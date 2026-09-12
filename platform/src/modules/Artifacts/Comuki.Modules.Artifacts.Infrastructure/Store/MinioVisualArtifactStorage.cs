using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// Low-level MinIO operations for the visual-artifact object store:
/// put, get, build key. Reuses the singleton <see cref="IMinioClient"/>
/// already registered for the run-artifact store — no second client,
/// no second connection pool. Object keys are
/// <c>visual_artifacts/{projectId}/{artifactId}/{version}</c>; the
/// bucket is the same <see cref="ArtifactsOptions.Bucket"/> every
/// artifact object lands in.
/// </summary>
/// <param name="client">MinIO SDK client (singleton, thread-safe).</param>
/// <param name="options">Bound MinIO connection options.</param>
/// <param name="logger">Structured logger.</param>
public sealed class MinioVisualArtifactStorage(
    IMinioClient client,
    IOptions<ArtifactsOptions> options,
    ILogger<MinioVisualArtifactStorage> logger)
{
    /// <summary>
    /// Uploads the artifact body to MinIO. Streams the body — the SDK
    /// does not buffer. The store counts bytes from the SDK's
    /// <c>StatObject</c> response to populate the metadata row.
    /// </summary>
    /// <param name="projectId">Owning project — scopes the object key.</param>
    /// <param name="artifactId">Artifact id — second scope.</param>
    /// <param name="version">Monotonic per-id version.</param>
    /// <param name="contentType">MIME type written to the object metadata.</param>
    /// <param name="sizeBytes">Pre-counted body length.</param>
    /// <param name="body">Body stream — the SDK reads it to EOF.</param>
    /// <param name="cancellationToken"></param>
    public async Task UploadAsync(
        ProjectId projectId,
        VisualArtifactId artifactId,
        int version,
        string contentType,
        long sizeBytes,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        var objectKey = BuildObjectKey(projectId, artifactId, version);
        var bucket = options.Value.Bucket;

        var args = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(body)
            .WithObjectSize(sizeBytes)
            .WithContentType(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        await client.PutObjectAsync(args, cancellationToken);

        logger.LogDebug(
            "Uploaded visual artifact {ObjectKey} to bucket {Bucket} ({SizeBytes} bytes)",
            objectKey,
            bucket,
            sizeBytes);
    }

    /// <summary>
    /// Opens a stream over one version of the artifact body and reports
    /// the object size + the canonical content-type. Returns <c>null</c>
    /// when the artifact id does not exist (caller maps to 404). The
    /// returned stream is owned by the caller — it must be disposed.
    /// </summary>
    /// <param name="projectId">Owning project — scopes the object key.</param>
    /// <param name="artifactId">Artifact id — second scope.</param>
    /// <param name="version">Monotonic per-id version to fetch.</param>
    /// <param name="cancellationToken"></param>
    public async Task<VisualArtifactObject?> OpenReadAsync(
        ProjectId projectId,
        VisualArtifactId artifactId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var objectKey = BuildObjectKey(projectId, artifactId, version);
        var bucket = options.Value.Bucket;

        try
        {
            var statArgs = new StatObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey);
            var stat = await client.StatObjectAsync(statArgs, cancellationToken);

            // The MinIO SDK delivers the object body through the
            // WithCallbackStream callback (GetObjectAsync returns the
            // stat; the stream is populated when the response arrives).
            // We synchronously drain the callback's stream into a
            // memory buffer; the callback runs before GetObjectAsync
            // completes, so by the time we await past it the body is
            // fully read. Returning the buffered copy means the
            // controller's File() helper does not have to reason about
            // the SDK's internal stream lifecycle.
            var buffered = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithCallbackStream(stream =>
                {
                    using (stream)
                    {
                        stream.CopyTo(buffered);
                    }
                });
            await client.GetObjectAsync(getArgs, cancellationToken);

            buffered.Position = 0;
            return new VisualArtifactObject(
                Body: buffered,
                SizeBytes: stat.Size,
                ContentType: string.IsNullOrWhiteSpace(stat.ContentType)
                    ? "application/octet-stream"
                    : stat.ContentType);
        }
        catch (MinioException exception) when (IsMinioNotFound(exception))
        {
            return null;
        }
    }

    /// <summary>True when the SDK raised a not-found (object missing or bucket missing).</summary>
    /// <param name="exception"></param>
    internal static bool IsMinioNotFound(MinioException exception)
    {
        return exception.Message.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("NoSuchKey", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("NoSuchBucket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds the canonical object key for one artifact version.</summary>
    /// <param name="projectId"></param>
    /// <param name="artifactId"></param>
    /// <param name="version"></param>
    internal static string BuildObjectKey(ProjectId projectId, VisualArtifactId artifactId, int version)
    {
        return $"visual_artifacts/{projectId.Value:N}/{artifactId.Value:N}/{version}";
    }
}
