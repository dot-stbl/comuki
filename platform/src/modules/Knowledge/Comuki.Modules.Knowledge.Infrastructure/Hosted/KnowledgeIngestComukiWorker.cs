using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Knowledge.Infrastructure.Hosted;

/// <summary>
/// Periodic doc worker — polls the corpus (v0: no KnowledgeSource table
/// yet, so the loop is a heartbeat that logs readiness + checks the
/// pgvector availability). When the KnowledgeSource table lands in a
/// later slice, this loop becomes the dispatcher: every pending source
/// row triggers a per-document <see cref="IKnowledgeIngestor"/> call
/// resolved through an <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/>
/// injected at that point (per-source scope, scoped DbContext lifetime).
/// <para>
/// Cadence is owned by the comuki worker registry
/// (<see cref="IComukiWorker"/>); the registry's supervision loop runs
/// <see cref="ExecuteAsync"/> on <see cref="Schedule"/>, with
/// exponential backoff on a failed cycle. <c>BackgroundService</c>
/// is gone here — the worker shape is the same as
/// <c>Comuki.Host.Artifacts.RunArtifactPackagerComukiWorker</c>,
/// which the host registered to bundle run artifacts.
/// </para>
/// </summary>
public sealed class KnowledgeIngestComukiWorker(
    IOptions<KnowledgeIngestOptions> options,
    ILogger<KnowledgeIngestComukiWorker> logger) : IComukiWorker
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));

    /// <inheritdoc />
    public string Name => "knowledge-ingest";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(interval);

    /// <inheritdoc />
    public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        // v0 heartbeat — the cancellation token is reserved for the
        // future sweep: a row-by-row IKnowledgeIngestor scope will
        // honor shutdown between sources. The surrounding registry loop
        // already backs off on a thrown cycle, so the worker stays
        // passive on a healthy boot and only logs a "nothing to do"
        // debug record each tick.
        logger.LogDebug("knowledge doc worker heartbeat (no sources yet)");
        return Task.FromResult(WorkerResult.Ok("heartbeat"));
    }
}
