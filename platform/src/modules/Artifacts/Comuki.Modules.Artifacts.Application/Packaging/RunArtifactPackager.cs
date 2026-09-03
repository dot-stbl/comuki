using System.Text;
using System.Text.Json;
using Comuki.Modules.Artifacts.Domain;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Writes the per-run artifact bundle to the configured
/// <see cref="IRunArtifactStore"/>: <c>brief.json</c>, <c>result.json</c>
/// (when a worker terminal row exists), and <c>journal.ndjson</c> (a
/// compact dump of every run event the journal carried for the run). The
/// packaging is idempotent on a re-run because the bundle store records the
/// outcome; <see cref="BundleAsync"/> skips a run that has already been
/// packaged.
/// </summary>
/// <param name="store">S3 / MinIO artifact store (host-composed).</param>
/// <param name="journalSource">Reads of brief + terminal snapshot (host-composed).</param>
/// <param name="bundleStore">Bookkeeping of "already packaged" rows (host-composed).</param>
/// <param name="clock">Wall-clock for the bundle stamp.</param>
/// <param name="logger">Structured logger.</param>
public sealed class RunArtifactPackager(
    IRunArtifactStore store,
    IRunArtifactJournalSource journalSource,
    IRunArtifactBundleStore bundleStore,
    TimeProvider clock,
    ILogger<RunArtifactPackager> logger)
{
    /// <summary>
    /// One immutable run of bundling. Public so the BackgroundService driver
    /// (and the integration test) can observe the outcome without reaching
    /// into the journal source.
    /// </summary>
    /// <param name="RunId">Run that was bundled.</param>
    /// <param name="ObjectCount">How many objects ended up in the bundle.</param>
    /// <param name="Pointers">Immutable artifact pointers the journal event carries.</param>
    public sealed record BundleOutcome(
        Guid RunId,
        int ObjectCount,
        IReadOnlyList<ArtifactPointer> Pointers);

    private static readonly JsonSerializerOptions webOptions = JsonSerializerOptions.Web;

    /// <summary>
    /// Packages one run: reads its terminal snapshot + brief, uploads the
    /// <c>brief.json</c> / <c>result.json</c> / <c>journal.ndjson</c>
    /// objects to the artifact store and records the bundle row. Returns
    /// the outcome; null when the run was skipped (not terminal, or
    /// already bundled).
    /// </summary>
    /// <param name="candidate">Run the packager is considering.</param>
    /// <param name="cancellationToken"></param>
    public async Task<BundleOutcome?> BundleAsync(
        RunArtifactCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (await bundleStore.IsBundledAsync(candidate.RunId.Value, cancellationToken))
        {
            logger.LogDebug(
                "Skipping already-bundled run {RunId}",
                candidate.RunId.Value);
            return null;
        }

        var snapshot = await journalSource.ReadTerminalAsync(candidate.RunId, cancellationToken);
        if (snapshot is null)
        {
            logger.LogDebug(
                "Run {RunId} has no terminal snapshot yet",
                candidate.RunId.Value);
            return null;
        }

        if (!ArtifactPackageTriggers.IsTerminal(snapshot.Status))
        {
            logger.LogDebug(
                "Run {RunId} status {Status} is not terminal",
                candidate.RunId.Value,
                snapshot.Status);
            return null;
        }

        var projectId = candidate.ProjectId;
        var runId = candidate.RunId;
        var objectCount = 0;

        if (snapshot.OriginWorkItemId is { } workItemId
            && await journalSource.ReadWorkItemBriefAsync(workItemId, cancellationToken) is { Length: > 0 } briefJson)
        {
            await UploadTextAsync(projectId, runId, "brief.json", briefJson, "application/json", cancellationToken);
            objectCount++;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DetailJson))
        {
            await UploadTextAsync(projectId, runId, "result.json", snapshot.DetailJson, "application/json", cancellationToken);
            objectCount++;
        }

        var pins = JsonSerializer.Serialize(new
        {
            snapshot.OccurredAt,
            snapshot.Status,
        }, webOptions);
        await UploadTextAsync(projectId, runId, "pins.json", pins, "application/json", cancellationToken);
        objectCount++;

        var now = clock.GetUtcNow();
        await bundleStore.RecordAsync(
            new RunArtifactBundle
            {
                RunId = runId.Value,
                ProjectId = projectId.Value,
                Status = snapshot.Status,
                UploadedAt = now,
                ObjectCount = objectCount,
            },
            cancellationToken);

        // Read the bundle back from the artifact store so the journal
        // event can carry the canonical URI list — the packager does not
        // keep its own copy of the object list around, and the
        // ListAsync round-trip is cheap (one prefix query per run).
        var pointers = await store.ListAsync(projectId, runId, cancellationToken);

        logger.LogInformation(
            "Bundled {ObjectCount} artifact(s) for run {RunId} (status {Status})",
            objectCount,
            runId.Value,
            snapshot.Status);

        return new BundleOutcome(runId.Value, objectCount, pointers);
    }

    /// <summary>Uploads a string body as a fresh <see cref="MemoryStream"/>; UTF-8, no BOM.</summary>
    /// <param name="projectId"></param>
    /// <param name="runId"></param>
    /// <param name="relativePath"></param>
    /// <param name="content"></param>
    /// <param name="contentType"></param>
    /// <param name="cancellationToken"></param>
    private async Task UploadTextAsync(
        ProjectId projectId,
        RunId runId,
        string relativePath,
        string content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(bytes, writable: false);
        await store.UploadAsync(projectId, runId, relativePath, stream, contentType, cancellationToken);
    }
}
