using Comuki.Modules.Artifacts.Domain.VisualArtifacts;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts;

/// <summary>
/// Discriminated outcome of <see cref="VisualArtifactService.PublishFromWorkerAsync"/>.
/// The worker endpoint maps each variant onto an HTTP status code
/// (204 / 409 / 413 / 415).
/// </summary>
public abstract record VisualArtifactPublishOutcome
{
    private VisualArtifactPublishOutcome()
    {
    }

    /// <summary>The artifact was persisted; <see cref="Artifact"/> carries the row.</summary>
    /// <param name="Artifact"></param>
    public sealed record PublishedRecord(VisualArtifact Artifact) : VisualArtifactPublishOutcome;

    /// <summary>The work item is unknown, not leased, or the lease expired (HTTP 409).</summary>
    public sealed record NotOwned : VisualArtifactPublishOutcome;

    /// <summary>The payload is larger than the per-mime cap (HTTP 413).</summary>
    /// <param name="ContentType"></param>
    /// <param name="SizeBytes">Observed payload size.</param>
    /// <param name="MaxBytes">Configured cap for the MIME type.</param>
    public sealed record SizeExceeded(string ContentType, long SizeBytes, long MaxBytes) : VisualArtifactPublishOutcome;

    /// <summary>The MIME type is not on the allow-list (HTTP 415).</summary>
    /// <param name="ContentType"></param>
    public sealed record UnsupportedMime(string ContentType) : VisualArtifactPublishOutcome;

    /// <summary>Sentinel for <see cref="NotOwned"/>.</summary>
    public static readonly VisualArtifactPublishOutcome NotOwner = new NotOwned();

    /// <summary>Wraps a <see cref="PublishedRecord"/>.</summary>
    /// <param name="artifact"></param>
    public static VisualArtifactPublishOutcome Published(VisualArtifact artifact)
    {
        return new PublishedRecord(artifact);
    }

    /// <summary>Wraps an <see cref="UnsupportedMime"/>.</summary>
    /// <param name="contentType"></param>
    public static VisualArtifactPublishOutcome RejectedMime(string contentType)
    {
        return new UnsupportedMime(contentType);
    }

    /// <summary>Wraps a <see cref="SizeExceeded"/>.</summary>
    /// <param name="contentType"></param>
    /// <param name="sizeBytes"></param>
    /// <param name="maxBytes"></param>
    public static VisualArtifactPublishOutcome RejectedSize(string contentType, long sizeBytes, long maxBytes)
    {
        return new SizeExceeded(contentType, sizeBytes, maxBytes);
    }
}
