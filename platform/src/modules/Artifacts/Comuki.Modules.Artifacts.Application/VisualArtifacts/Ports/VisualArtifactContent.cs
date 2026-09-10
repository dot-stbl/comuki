namespace Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;

/// <summary>
/// The bytes of an artifact + the MIME type the proxy serves them
/// under. <see cref="Body"/> is a freshly-opened stream from MinIO;
/// the caller disposes it.
/// </summary>
/// <param name="Body">Object body — read to EOF by the caller.</param>
/// <param name="ContentType">MIME type from the metadata row.</param>
/// <param name="SizeBytes">Object size in bytes.</param>
public sealed record VisualArtifactContent(Stream Body, string ContentType, long SizeBytes);
