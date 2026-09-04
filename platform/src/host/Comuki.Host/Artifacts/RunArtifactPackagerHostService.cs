using System.Text.Json;
using Comuki.Host.OpenApi;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host driver for the artifact packager (issue #28). Delegates one poll
/// cycle to the in-module <see cref="RunArtifactPackagerService"/> and
/// appends a <c>run.artifacts_bundled</c> journal entry per successful
/// bundle, carrying the canonical <see cref="ArtifactPointer"/> list the
/// dashboard's Artifacts tab renders. The journal emission lives in the
/// host so the artifacts module has no project reference on the engine.
///
/// During build-time OpenAPI document generation
/// (<see cref="OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration"/>)
/// the background loop is a no-op: the document tool only needs to
/// enumerate endpoints and exits before the first poll fires.
/// </summary>
/// <param name="scopeFactory">Scope factory — the per-poll scope gives the packager and the journal append their own DbContexts.</param>
/// <param name="clock">Wall-clock for the journal stamp.</param>
/// <param name="logger">Structured logger.</param>
public sealed class RunArtifactPackagerHostService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<RunArtifactPackagerHostService> logger) : BackgroundService
{
    /// <summary>Stable journal event type for the host-side bundle completion marker.</summary>
    public const string BundleJournalType = "run.artifacts_bundled";

    /// <summary>Default poll interval — mirrors the in-module driver.</summary>
    public static readonly TimeSpan DefaultPollInterval = RunArtifactPackagerService.DefaultPollInterval;

    private static readonly TimeSpan pollInterval = DefaultPollInterval;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (OpenApiBuildTimeExtensions.IsOpenApiDocumentGeneration)
        {
            logger.LogInformation(
                "Run artifact packager host service skipped under OpenAPI document generation");
            return;
        }

        logger.LogInformation(
            "Run artifact packager host service started (interval {Interval}s)",
            pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Run artifact packager host poll failed; will retry after {Interval}s",
                    pollInterval.TotalSeconds);
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Run artifact packager host service stopped");
    }

    /// <summary>
    /// Runs one poll cycle and appends a <c>run.artifacts_bundled</c> entry
    /// to the run journal per successful bundle. Exposed so integration
    /// tests can drive the packager deterministically without waiting for
    /// the timer. Returns the per-run outcomes (already-bundled skips
    /// drop out as null entries and are filtered).
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<RunArtifactPackager.BundleOutcome>> PollOnceAsync(CancellationToken cancellationToken)
    {
        var outcomes = new List<RunArtifactPackager.BundleOutcome>();

        await using var packagerScope = scopeFactory.CreateAsyncScope();
        var packager = packagerScope.ServiceProvider.GetRequiredService<RunArtifactPackagerService>();
        var bundledOutcomes = await packager.PollOnceAsync(cancellationToken);

        foreach (var outcome in bundledOutcomes)
        {
            outcomes.Add(outcome);
            await BundleJournalAppender.TryAppendAsync(
                scopeFactory,
                clock,
                logger,
                BundleJournalType,
                outcome,
                cancellationToken);
        }

        return outcomes;
    }
}

/// <summary>
/// File-scoped helper that appends one <c>run.artifacts_bundled</c> journal
/// entry per successful artifact bundle. Uses an isolated DI scope per
/// entry so the journal context is not the one the packager just
/// disposed. Errors are logged and swallowed — the bundle is already in
/// MinIO and the bookkeeping row is recorded, so a journal failure must
/// not stall the loop.
/// </summary>
file static class BundleJournalAppender
{
    public static async Task TryAppendAsync(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger logger,
        string bundleJournalType,
        RunArtifactPackager.BundleOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var journalScope = scopeFactory.CreateAsyncScope();
            var journal = journalScope.ServiceProvider.GetRequiredService<IRunJournal>();
            var payload = JsonSerializer.Serialize(
                new BundleJournalPayload(outcome.RunId, outcome.ObjectCount, [.. outcome.Pointers]),
                JsonSerializerOptions.Web);

            await journal.AppendAsync(
                new RunEventEntry(
                    Id: Guid.NewGuid(),
                    RunId: new RunId(outcome.RunId),
                    Type: bundleJournalType,
                    PayloadJson: payload,
                    OccurredAt: clock.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to append {EventType} journal entry for run {RunId}",
                bundleJournalType,
                outcome.RunId);
        }
    }

    /// <summary>jsonb payload of the <c>run.artifacts_bundled</c> journal entry.</summary>
    /// <param name="RunId">Run that was bundled.</param>
    /// <param name="ObjectCount">How many objects the bundle ended up holding.</param>
    /// <param name="Pointers">Canonical URI list as returned by the artifact store.</param>
    internal sealed record BundleJournalPayload(Guid RunId, int ObjectCount, ArtifactPointer[] Pointers);
}
