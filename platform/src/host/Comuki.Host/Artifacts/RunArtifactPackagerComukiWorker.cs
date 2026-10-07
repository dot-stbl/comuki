using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host driver for the run-artifact packager: drives
/// <see cref="RunArtifactPackagerService.PollOnceAsync"/> on a fixed
/// interval and appends a <c>run.artifacts_bundled</c> journal event
/// in the same transaction the module uses for the bundle row. Lives
/// in the host composition root so the engine schema's append is
/// owned by the host (the artifacts module never reaches into it).
/// Scoped journal access through <see cref="IServiceScopeFactory"/>
/// — each cycle creates its own orchestration context.
/// <para>
/// The cadence is owned by the comuki worker registry, not by
/// <see cref="BackgroundService"/>: the
/// <see cref="IComukiWorker"/> shape
/// gives the host one supervision loop per worker, with
/// exponential backoff on a failed cycle (an <c>HttpRequestException</c>
/// against MinIO counts as transient; an unhandled <c>JsonException</c>
/// also backs off rather than spins the registry into a tight retry).
/// </para>
/// </summary>
/// <param name="scopeFactory">Scope factory for the journal + orchestration contexts.</param>
/// <param name="clock">Wall-clock for the journal event stamp.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the journal write.</param>
public sealed class RunArtifactPackagerComukiWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ISubjectScopeAccessor scopeAccessor) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "artifact-packager";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(RunArtifactPackagerService.DefaultPollInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await PollOnceAsync(cancellationToken);
            return WorkerResult.Ok("cycle complete");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                   or TaskCanceledException or JsonException)
        {
            // boundary: the worker's own supervision loop — transient
            // MinIO / orchestration failures count as a failed cycle for
            // the registry (logged, exponential backoff). Next cycle
            // retries the whole batch; per-candidate isolation lives
            // inside PollOnceAsync, so one bad run never blocks the rest.
            return WorkerResult.Fail($"cycle failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Runs one polling cycle synchronously — exposed for integration tests
    /// that need to drive the packager deterministically rather than wait
    /// for the 10-second interval.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        await using var cycleScope = scopeFactory.CreateAsyncScope();
        var packagerService = cycleScope.ServiceProvider.GetRequiredService<RunArtifactPackagerService>();
        var outcomes = await packagerService.PollOnceAsync(cancellationToken);

        if (outcomes.Count == 0)
        {
            return;
        }

        using var systemScope = scopeAccessor.AsSystem(Name);

        await using var journalScope = scopeFactory.CreateAsyncScope();
        var db = journalScope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        var now = clock.GetUtcNow();
        foreach (var outcome in outcomes)
        {
            var payload = JsonSerializer.Serialize(
                new ArtifactBundledPayload(
                    [.. outcome.Pointers.Select(pointer => new BundledPointer(pointer.Name, pointer.Uri.ToString()))],
                    outcome.ObjectCount),
                JsonSerializerOptions.Web);

            db.RunEvents.Add(RunEvent.Create(
                new RunId(outcome.RunId),
                ArtifactEventTypes.ArtifactsBundled,
                payload,
                now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Stable journal event type for bundled run artifacts (read by the realtime broadcaster).</summary>
public static class ArtifactEventTypes
{
    public const string ArtifactsBundled = "run.artifacts_bundled";
}

/// <summary>Pointer projection the journal event carries — minimal subset for FE + UI consumers.</summary>
/// <param name="ObjectName">Object name under the run's prefix.</param>
/// <param name="CanonicalUri">Signed / canonical URI the host can fetch.</param>
internal sealed record BundledPointer(string ObjectName, string CanonicalUri);

/// <summary>Run artifact bundle payload.</summary>
/// <param name="Pointers">Canonical artifact pointer list.</param>
/// <param name="ObjectCount">Number of objects the packager uploaded for the run.</param>
internal sealed record ArtifactBundledPayload(BundledPointer[] Pointers, int ObjectCount);
