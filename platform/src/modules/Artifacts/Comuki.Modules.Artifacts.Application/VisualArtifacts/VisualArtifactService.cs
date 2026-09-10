using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Telemetry;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts;

/// <summary>
/// Application-layer entry point for publishing a visual artifact.
/// Owns the size cap, the content-type whitelist, the journal event
/// emission, and the metric — the host endpoint hands the raw bytes
/// in, this service hands the persisted row back.
/// </summary>
/// <param name="store">EF + MinIO-backed store.</param>
/// <param name="workItemSource">Host-composed lease / ownership probe.</param>
/// <param name="journal">Run-journal writer — only invoked when the artifact is linked to a run.</param>
/// <param name="clock">Wall-clock for the publish stamp + telemetry.</param>
/// <param name="logger">Structured logger.</param>
public sealed class VisualArtifactService(
    IVisualArtifactStore store,
    IWorkItemArtifactSource workItemSource,
    IRunJournal journal,
    TimeProvider clock,
    ILogger<VisualArtifactService> logger)
{
    /// <summary>
    /// Publishes an artifact from a worker that holds the lease on
    /// <paramref name="workItemId"/>. The service stamps the work
    /// item + run id from the lease probe; the caller supplies the
    /// project-scoped bytes (already size-counted) plus the wire
    /// metadata (filename + content type).
    /// </summary>
    /// <param name="workItemId">Work item the worker is leased to.</param>
    /// <param name="workerId">Worker id from the bearer token.</param>
    /// <param name="filename">Original filename from the multipart upload.</param>
    /// <param name="contentType">MIME type from the multipart upload.</param>
    /// <param name="sizeBytes">Pre-counted body length — the service trusts this and rejects payloads above the cap.</param>
    /// <param name="body">The body stream — the service does not buffer it.</param>
    /// <param name="title">Optional human title.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The persisted artifact row, or a typed result describing why the request was rejected.</returns>
    public async Task<VisualArtifactPublishOutcome> PublishFromWorkerAsync(
        Guid workItemId,
        WorkerId workerId,
        string filename,
        string contentType,
        long sizeBytes,
        Stream body,
        string? title,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        if (!VisualArtifactLimits.IsAllowedMime(contentType))
        {
            return VisualArtifactPublishOutcome.RejectedMime(contentType);
        }

        var cap = VisualArtifactLimits.MaxBytesFor(contentType);
        if (cap is { } limit && sizeBytes > limit)
        {
            return VisualArtifactPublishOutcome.RejectedSize(contentType, sizeBytes, limit);
        }

        var ownership = await workItemSource.FindOwnedAsync(workItemId, workerId, now, cancellationToken);
        if (ownership is null)
        {
            return VisualArtifactPublishOutcome.NotOwner;
        }

        var artifact = await store.UploadVisualAsync(
            new VisualArtifactUploadRequest(
                ProjectId: ownership.ProjectId,
                Filename: filename,
                ContentType: contentType,
                SizeBytes: sizeBytes,
                Body: body,
                CreatedBy: VisualArtifactCreatedByWire.Worker,
                CreatedAt: now,
                RunId: ownership.RunId.Value,
                WorkItemId: workItemId,
                Title: title),
            cancellationToken);

        await VisualArtifactServiceHelpers.AppendPublishedJournalEntryAsync(
            journal,
            artifact,
            ownership.RunId,
            clock.GetUtcNow(),
            cancellationToken);

        ComukiTelemetry.VisualArtifactsPublished.Add(1,
            new KeyValuePair<string, object?>(ComukiInstrumentation.ContentTypeTag, contentType),
            new KeyValuePair<string, object?>(ComukiInstrumentation.SourceTag, ComukiInstrumentation.SourceWorker));
        ComukiTelemetry.VisualArtifactBytes.Record(sizeBytes,
            new KeyValuePair<string, object?>(ComukiInstrumentation.ContentTypeTag, contentType));

        logger.LogInformation(
            "Published visual artifact {ArtifactId} ({ContentType}, {SizeBytes} bytes) on run {RunId}",
            artifact.Id,
            contentType,
            sizeBytes,
            ownership.RunId.Value);

        return VisualArtifactPublishOutcome.Published(artifact);
    }
}

/// <summary>
/// File-static helpers for <see cref="VisualArtifactService"/>. Extracted
/// so the service class holds only orchestration — every member on the
/// service itself is part of its public contract. Per
/// <c>class-layout-and-tooling.md §1a</c>, helper logic lives in
/// <c>file static class</c>es next to the consumer.
/// </summary>
file static class VisualArtifactServiceHelpers
{
    /// <summary>
    /// Appends an <c>artifact.published</c> entry to the run timeline.
    /// Id-only payload — the dashboard re-fetches the artifact's
    /// metadata from the content proxy; signed URLs would expire and
    /// the journal is forever.
    /// </summary>
    /// <param name="journal">Run-journal writer.</param>
    /// <param name="artifact">Persisted artifact row.</param>
    /// <param name="runId">Parent run.</param>
    /// <param name="occurredAt">Wall-clock the entry is stamped with.</param>
    /// <param name="cancellationToken"></param>
    public static async Task AppendPublishedJournalEntryAsync(
        IRunJournal journal,
        VisualArtifact artifact,
        RunId runId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var payload = new RunJournalPayloads.ArtifactPublishedPayload(
            ArtifactId: artifact.Id,
            ContentType: artifact.ContentType,
            Filename: artifact.Filename,
            SizeBytes: artifact.SizeBytes);

        var entry = new RunEventEntry(
            Id: Guid.CreateVersion7(),
            RunId: runId,
            Type: VisualArtifactEvents.Published,
            PayloadJson: RunJournalPayloads.SerialiseArtifactPublished(payload),
            OccurredAt: occurredAt);

        await journal.AppendAsync(entry, cancellationToken);
    }
}
