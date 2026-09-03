namespace Comuki.Shared.Contracts.Artifacts;

/// <summary>One object inside a run's artifact bundle. URIs, not blobs.</summary>
/// <param name="Name">Object name under the run prefix (e.g. <c>brief.json</c>).</param>
/// <param name="Uri">Canonical URI the host can fetch (typically a MinIO signed URL).</param>
/// <param name="Size">Object size in bytes; <c>0</c> if unknown.</param>
/// <param name="ContentType">MIME type as written at upload time.</param>
public sealed record ArtifactPointer(
    string Name,
    Uri Uri,
    long Size,
    string ContentType);
