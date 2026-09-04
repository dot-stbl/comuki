using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Artifacts.Application.Packaging;

/// <summary>
/// Polling driver for <see cref="RunArtifactPackager"/>. Wakes up on a
/// fixed interval, asks the run source for terminal-but-not-bundled runs
/// and bundles each one. Errors on one run do not stop the loop — the
/// exception is logged and the driver moves on, so a transient MinIO
/// outage does not stall the queue.
/// </summary>
/// <remarks>
/// Each polling cycle runs in two phases, each with its own DI scope, to
/// avoid the "A command is already in progress" race the previous
/// single-scope design hit against Npgsql:
/// <list type="number">
///   <item>
///     <description>Discovery — opens a short-lived scope, drains the run
///     source's async stream into an in-memory list, then disposes the
///     scope. The discovery scope's <c>OrchestrationDbContext</c> is
///     released back to the pool before the bundle phase starts.</description>
///   </item>
///   <item>
///     <description>Bundling — opens one fresh scope per candidate and
///     resolves the packager from that scope. The packager's journal
///     source and bundle store share the scope and therefore its
///     <c>OrchestrationDbContext</c> / <c>ArtifactsDbContext</c>; per-
///     candidate scopes prevent two bundle operations from racing on
///     a single DbContext. The bundle scope is wrapped in an
///     <c>AsSystem("artifact-packager")</c> subject scope so the
///     scoped query filters in the orchestration DbContext resolve
///     without a request-scoped subject.</description>
///   </item>
/// </list>
/// </remarks>
/// <param name="scopeFactory">Per-phase and per-candidate scope factory.</param>
/// <param name="scopeAccessor">Ambient subject scope — the bundler runs as a background consumer.</param>
/// <param name="logger">Structured logger.</param>
public sealed class RunArtifactPackagerService(
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    ILogger<RunArtifactPackagerService> logger) : BackgroundService
{
    /// <summary>Default poll interval — overridable by tests via the constructor.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(10);

    /// <summary>Default per-poll cap on candidates processed.</summary>
    public const int DefaultBatchLimit = 50;

    private static readonly TimeSpan pollInterval = DefaultPollInterval;
    private const int BatchLimit = DefaultBatchLimit;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Run artifact packager started (interval {Interval}s, batch {Batch})",
            pollInterval.TotalSeconds,
            BatchLimit);

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
                    "Run artifact packager poll failed; will retry after {Interval}s",
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

        logger.LogInformation("Run artifact packager stopped");
    }

    /// <summary>
    /// Runs one polling cycle — exposed for integration tests that need to
    /// drive the packager deterministically. Returns the per-run outcomes;
    /// null entries (skip / already-bundled) are dropped.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task<IReadOnlyList<RunArtifactPackager.BundleOutcome>> PollOnceAsync(CancellationToken cancellationToken)
    {
        // Phase 1 — discovery: open a short-lived scope, drain the run
        // source's async stream into an in-memory list while the
        // underlying connection is still open, then dispose the scope so
        // the discovery DbContext goes back to the pool. The bundle
        // phase below opens its own connection(s) and never has to share
        // a DbContext with the stream.
        var candidates = await DiscoverCandidatesAsync(BatchLimit, cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        // Phase 2 — bundle each candidate in its own fresh scope. The
        // packager, journal source and bundle store all resolve from the
        // same scope and therefore see the same DbContexts. Per-
        // candidate scopes prevent one bundle's DbContext from being
        // used by the next bundle's read or write, which was the race
        // condition the single-scope design exposed under high write
        // load.
        var outcomes = new List<RunArtifactPackager.BundleOutcome>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await BundleOneAsync(candidate, cancellationToken);
            if (outcome is not null)
            {
                outcomes.Add(outcome);
            }
        }

        if (outcomes.Count > 0)
        {
            logger.LogInformation("Run artifact packager bundled {Count} run(s) in this cycle", outcomes.Count);
        }

        return outcomes;
    }

    /// <summary>
    /// Phase 1: open a fresh scope, resolve the run source, drain its
    /// async stream into a list, and dispose the scope. Materialising
    /// the stream inside the scope guarantees the connection-yielding
    /// EF query stays alive for every yielded row; disposing the scope
    /// returns the connection to the pool before the bundle phase opens
    /// new ones.
    /// </summary>
    /// <param name="limit">Maximum number of candidates to discover.</param>
    /// <param name="cancellationToken"></param>
    private async Task<List<RunArtifactCandidate>> DiscoverCandidatesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var discoveryScope = scopeFactory.CreateAsyncScope();
        var runSource = discoveryScope.ServiceProvider.GetRequiredService<IRunArtifactRunSource>();

        var candidates = new List<RunArtifactCandidate>(capacity: Math.Min(limit, 16));
        await foreach (var candidate in runSource.ListUnbundledTerminalAsync(limit, cancellationToken))
        {
            candidates.Add(candidate);
        }

        return candidates;
    }

    /// <summary>
    /// Phase 2 (one call per candidate): open a fresh scope, resolve the
    /// packager from that scope, call
    /// <see cref="RunArtifactPackager.BundleAsync"/>, and dispose the
    /// scope. The packager, journal source and bundle store share the
    /// scope and therefore its DbContexts; a separate scope per
    /// candidate prevents the "A command is already in progress" Npgsql
    /// race that the previous single-scope design hit. The scope is
    /// wrapped in an <c>AsSystem("artifact-packager")</c> subject scope
    /// so the orchestration DbContext's tenant-scoped query filters
    /// resolve without a request-scoped subject.
    /// </summary>
    /// <param name="candidate">Run to bundle in this iteration.</param>
    /// <param name="cancellationToken"></param>
    private async Task<RunArtifactPackager.BundleOutcome?> BundleOneAsync(
        RunArtifactCandidate candidate,
        CancellationToken cancellationToken)
    {
        using var systemScope = scopeAccessor.AsSystem("artifact-packager");
        await using var bundleScope = scopeFactory.CreateAsyncScope();
        var packager = bundleScope.ServiceProvider.GetRequiredService<RunArtifactPackager>();

        return await packager.BundleAsync(candidate, cancellationToken);
    }
}
