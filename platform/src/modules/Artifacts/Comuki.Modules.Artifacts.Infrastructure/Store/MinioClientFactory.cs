using Minio;

namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>
/// Builds a configured <see cref="IMinioClient"/> from the bound
/// <see cref="ArtifactsOptions"/>. Kept separate so the factory can be
/// unit-tested without spinning up a DI container, and so the registration
/// extension is a one-liner.
/// </summary>
public sealed class MinioClientFactory
{
    /// <summary>
    /// Returns an <see cref="IMinioClient"/> wired against the configured
    /// endpoint + credentials + TLS mode. The builder is reused across
    /// uploads (the SDK is thread-safe); callers must NOT mutate the
    /// returned client.
    /// </summary>
    /// <param name="options">Bound MinIO connection options.</param>
    public static IMinioClient Create(ArtifactsOptions options)
    {
        var client = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey);

        if (options.UseSSL)
        {
            client = client.WithSSL();
        }

        return client.Build();
    }
}
