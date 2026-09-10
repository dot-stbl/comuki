namespace Comuki.Modules.Artifacts.Infrastructure.Store;

/// <summary>The bytes of a visual artifact + the canonical metadata read from MinIO.</summary>
/// <param name="Body">Object body — read to EOF by the caller. Already buffered.</param>
/// <param name="SizeBytes">Object size in bytes (MinIO-reported).</param>
/// <param name="ContentType">MIME type written to the object metadata.</param>
public sealed record VisualArtifactObject(Stream Body, long SizeBytes, string ContentType);
