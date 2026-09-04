using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Host driver for the run-artifact packager: wraps the in-module polling
/// helper and appends a <c>run.artifacts_bundled</c> journal event in the
/// same transaction the module uses for the bundle row. Lives in the
/// host composition root so the engine schema's append is owned by the
/// host (the artifacts module never reaches into it). Scoped journal
/// access through <see cref="IServiceScopeFactory"/> — each cycle creates
/// its own orchestration context.
/// </summary>
/// <param name="scopeFactory">Scope factory for the journal + orchestration contexts.</param>
/// <param name="clock">Wall-clock for the journal event stamp.</param>
/// <param name="scopeAccessor">Ambient scope — declare system for the journal write.</param>
/// <param name="logger">Structured logger.</param>
public sealed class RunArtifactPackagerHostService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ISubjectScopeAccessor scopeAccessor,
    ILogger<RunArtifactPackagerHostService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Run artifact packager host driver started");

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
                logger.LogError(exception, "Run artifact packager cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Run artifact packager host driver stopped");
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

        using var systemScope = scopeAccessor.AsSystem("artifact-packager");

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
