using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// MinIO-backed implementation of <see cref="IRunArtifactStore"/>. Every
/// object key is prefixed with <c>{projectId}/{runId}/</c>; the bucket is
/// the single, configured <see cref="ArtifactsOptions.Bucket"/>. Bucket
/// provisioning is the host's job (the compose <c>minio-init</c> job or
/// the in-process <c>EnsureBucketAsync</c> when
/// <see cref="ArtifactsOptions.AutoCreateBucket"/> is on).
/// </summary>
/// <param name="client">MinIO SDK client (singleton, thread-safe).</param>
/// <param name="options">Bound MinIO connection options.</param>
/// <param name="logger">Structured logger.</param>
public sealed class MinioRunArtifactStore(
    IMinioClient client,
    IOptions<ArtifactsOptions> options,
    ILogger<MinioRunArtifactStore> logger) : IRunArtifactStore
{
    private readonly ArtifactsOptions configuration = options.Value;

    /// <inheritdoc />
    public async Task<Uri> UploadAsync(
        ProjectId projectId,
        RunId runId,
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var objectKey = BuildObjectKey(projectId, runId, relativePath);

        var args = new PutObjectArgs()
            .WithBucket(configuration.Bucket)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(content.Length)
            .WithContentType(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        await client.PutObjectAsync(args, cancellationToken);

        logger.LogDebug(
            "Uploaded artifact {ObjectKey} to bucket {Bucket} ({Size} bytes)",
            objectKey,
            configuration.Bucket,
            content.Length);

        return BuildObjectUri(objectKey);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArtifactPointer>> ListAsync(
        ProjectId projectId,
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildRunPrefix(projectId, runId) + "/";
        var pointers = new List<ArtifactPointer>();

        var listArgs = new ListObjectsArgs()
            .WithBucket(configuration.Bucket)
            .WithPrefix(prefix)
            .WithRecursive(true);

        await foreach (var item in client.ListObjectsEnumAsync(listArgs, cancellationToken))
        {
            if (string.IsNullOrEmpty(item.Key))
            {
                continue;
            }

            var name = item.Key.Length > prefix.Length
                ? item.Key[prefix.Length..]
                : item.Key;

            pointers.Add(new ArtifactPointer(
                Name: name,
                Uri: BuildObjectUri(item.Key),
                SizeBytes: (long)item.Size,
                ContentType: item.ContentType ?? "application/octet-stream"));
        }

        return pointers;
    }

    /// <summary>
    /// Idempotently ensures the configured bucket exists. Called by the host
    /// at startup when <see cref="ArtifactsOptions.AutoCreateBucket"/> is on
    /// (e.g. the integration test against a fresh Testcontainers MinIO).
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        var exists = await client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(configuration.Bucket),
            cancellationToken);
        if (exists)
        {
            return;
        }

        try
        {
            await client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(configuration.Bucket),
                cancellationToken);
            logger.LogInformation("Created MinIO bucket {Bucket}", configuration.Bucket);
        }
        catch (MinioException exception) when (IsAlreadyExists(exception))
        {
            // Race: another instance just created it.
            logger.LogDebug("MinIO bucket {Bucket} already existed (race)", configuration.Bucket);
        }
    }

    /// <summary>Builds the canonical object key for one artifact.</summary>
    /// <param name="projectId"></param>
    /// <param name="runId"></param>
    /// <param name="relativePath"></param>
    /// <exception cref="ArgumentException"></exception>
    internal static string BuildObjectKey(ProjectId projectId, RunId runId, string relativePath)
    {
        // Validate the relative path. The three rules below are independent
        // and order-sensitive (empty check first — null/white would slip
        // through StartsWith and Contains); the IDE0046 "use pattern" hint
        // is suppressed per the project rule that bans ArgumentException.ThrowIf*
        // and prefers a single explicit message per invalid path.
#pragma warning disable IDE0046
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("relative path must not be empty", nameof(relativePath));
        }

        if (relativePath.StartsWith('/'))
        {
            throw new ArgumentException("relative path must not start with a slash", nameof(relativePath));
        }

        if (relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("relative path must not contain '..' segments", nameof(relativePath));
        }
#pragma warning restore IDE0046

        return $"{BuildRunPrefix(projectId, runId)}/{relativePath}";
    }

    /// <summary>Builds the <c>{projectId}/{runId}</c> prefix that scopes every object under one run.</summary>
    /// <param name="projectId"></param>
    /// <param name="runId"></param>
    internal static string BuildRunPrefix(ProjectId projectId, RunId runId)
    {
        return $"{projectId.Value:N}/{runId.Value:N}";
    }

    /// <summary>Canonical object URI — the MinIO SDK does not expose the host:port directly, so we construct from options.</summary>
    /// <param name="objectKey"></param>
    private Uri BuildObjectUri(string objectKey)
    {
        var scheme = configuration.UseSSL ? "https" : "http";
        return new Uri($"{scheme}://{configuration.Endpoint}/{configuration.Bucket}/{objectKey}");
    }

    /// <summary>True when the SDK raised "bucket already exists" — a race, not a failure.</summary>
    /// <param name="exception"></param>
    private static bool IsAlreadyExists(MinioException exception)
    {
        return exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
