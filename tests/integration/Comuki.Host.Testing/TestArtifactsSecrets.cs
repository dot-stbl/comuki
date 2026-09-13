using Microsoft.Extensions.Configuration;

namespace Comuki.Host.Testing;

/// <summary>
/// Non-dev-default Artifacts secrets every integration suite configures so
/// <c>ProductionSecretValidator</c> (issue #10 T11.4) passes — even for
/// suites that never boot a real MinIO container.
/// </summary>
public static class TestArtifactsSecrets
{
    public const string AccessKey = "test-access-key";
    public const string SecretKey = "test-secret-key-with-enough-entropy";
    public const string Bucket = "comuki-test-bundles";

    /// <summary>Endpoint placeholder for suites that never dial MinIO for real — the validator only checks the values are non-dev-defaults.</summary>
    public const string PlaceholderEndpoint = "minio:9000";

    /// <summary>Applies the placeholder endpoint plus the standard credentials — for suites with no real MinIO container.</summary>
    public static void ApplyPlaceholder(IConfiguration configuration)
    {
        configuration["Artifacts:Endpoint"] = PlaceholderEndpoint;
        configuration["Artifacts:AccessKey"] = AccessKey;
        configuration["Artifacts:SecretKey"] = SecretKey;
        configuration["Artifacts:Bucket"] = Bucket;
    }
}
