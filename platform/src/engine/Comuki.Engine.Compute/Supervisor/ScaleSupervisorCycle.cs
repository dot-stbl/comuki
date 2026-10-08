using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Pool;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Scaling;
using Comuki.Engine.Compute.Security;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Supervisor;

/// <summary>
/// One scale supervisor pass (issue #3 T2.4/T2.5; add-worker-environments task
/// 3.4): reconcile the worker pool with the provider, read the backlog per
/// (profile, env class) pair, apply the pure scale policy, then start
/// workers (token via <see cref="WorkerTokenIssuer"/>, image from the
/// catalog bundle the class resolves to — never a per-project image
/// override of a different class, profiles-ref from per-project settings
/// falling back to supervisor options) and reap stale idle ones with
/// <see cref="ComputeStopReason.IdleTtl"/>. All I/O goes through injected
/// ports — unit tests drive it with fakes.
/// </summary>
/// <param name="scaleOptions"></param>
/// <param name="backlogReader"></param>
/// <param name="pool"></param>
/// <param name="tokenIssuer"></param>
/// <param name="projectScaleSettings"></param>
/// <param name="computeProvider"></param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class ScaleSupervisorCycle(
    IOptions<ScaleSupervisorOptions> scaleOptions,
    IBacklogReader backlogReader,
    WorkerPoolState pool,
    WorkerTokenIssuer tokenIssuer,
    IProjectScaleSettings projectScaleSettings,
    IComputeProvider computeProvider,
    TimeProvider clock,
    ILogger<ScaleSupervisorCycle> logger)
{
    /// <summary>Runs one pass over every configured project and profile.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var options = scaleOptions.Value;
        // Capacity is a provider-wide hint (one call per pass). Remaining
        // free slots shrink as this pass starts workers so later projects /
        // profiles cannot overshoot the same cluster headroom.
        var remainingFreeSlots = (await computeProvider.GetCapacityAsync(cancellationToken)).FreeSlots;

        foreach (var projectValue in options.Projects)
        {
            var projectId = new ProjectId(projectValue);
            await pool.SyncFromProviderAsync(projectId, cancellationToken);

            var settings = projectScaleSettings.Get(projectId);

            // Per-project env class binding (add-worker-environments task 3.4):
            // null means "use the supervisor's default class" — the bridge
            // returns null when the project has no binding, and the supervisor
            // (not the provider) owns the fallback so the policy inputs and
            // the started stamp agree.
            var effectiveEnvClass = settings.EnvClass ?? options.DefaultEnvClass;

            foreach (var profileKey in options.ProfileKeys)
            {
                var queuedCount = await backlogReader.CountQueuedAsync(projectId, profileKey, cancellationToken);
                var projectWorkers = pool.List(projectId);
                // Idle / coverage / reaper counts are per (profile, env class) pair —
                // workers of a different class never satisfy this pair's backlog
                // (worker-environments spec §"Scale policy per (profile, env class)").
                var pairWorkers = projectWorkers
                    .Where(worker => worker.ProfileKey == profileKey && worker.EnvClass == effectiveEnvClass)
                    .ToArray();
                var idleCount = pairWorkers.Count(worker => !worker.IsBusy);
                var staleIdleCount = pairWorkers.Count(
                    worker => !worker.IsBusy && clock.GetUtcNow() - worker.LastActiveAt > settings.IdleTtl);

                var decision = ScalePolicy.Decide(
                    new ScalePolicyInput(
                        queuedCount,
                        idleCount,
                        staleIdleCount,
                        projectWorkers.Count,
                        settings.MinIdle,
                        settings.MaxConcurrent,
                        remainingFreeSlots));
                if (decision.ClampedByCapacity)
                {
                    logger.LogWarning(
                        "Scale decision for project {ProjectId} profile {ProfileKey} class {EnvClass} clamped by provider capacity: freeSlots={FreeSlots}",
                        projectId.Value,
                        profileKey,
                        effectiveEnvClass,
                        remainingFreeSlots);
                }

                logger.LogInformation(
                    "Scale decision for project {ProjectId} profile {ProfileKey} class {EnvClass}: queued={QueuedCount} idle={IdleCount} staleIdle={StaleIdleCount} running={RunningCount} freeSlots={FreeSlots}; start={StartWorkers} stopIdle={StopIdleWorkers} clampedByCapacity={ClampedByCapacity}",
                    projectId.Value,
                    profileKey,
                    effectiveEnvClass,
                    queuedCount,
                    idleCount,
                    staleIdleCount,
                    projectWorkers.Count,
                    remainingFreeSlots,
                    decision.StartWorkers,
                    decision.StopIdleWorkers,
                    decision.ClampedByCapacity);

                for (var started = 0; started < decision.StartWorkers; started++)
                {
                    var tokenId = WorkerId.New();
                    var request = new ComputeStartRequest
                    {
                        ProjectId = projectId,
                        PreIssuedWorkerId = tokenId,
                        ProfileKey = profileKey,
                        ProfilesGitRef = settings.ProfilesGitRef ?? options.ProfilesGitRef,
                        EnvClass = effectiveEnvClass,
                        // Image is a placeholder — the provider resolves the actual image
                        // from the catalog bundle for the class (add-worker-environments
                        // spec §"class resolves to digest at start").
                        Image = "resolved-by-catalog",
                        WorkerToken = tokenIssuer.Issue(tokenId),
                        OrchestratorGrpcUrl = options.OrchestratorGrpcUrl,
                    };

                    var handle = await computeProvider.StartAsync(request, cancellationToken);
                    pool.Register(handle, tokenId, projectId, profileKey, effectiveEnvClass);
                    remainingFreeSlots = Math.Max(0, remainingFreeSlots - 1);
                    logger.LogInformation(
                        "Scale supervisor started worker {WorkerId} for project {ProjectId} profile {ProfileKey} class {EnvClass}",
                        handle.Id.Value,
                        projectId.Value,
                        profileKey,
                        effectiveEnvClass);
                }

                if (decision.StopIdleWorkers is 0)
                {
                    continue;
                }

                var staleWorkers = pool.List(projectId)
                    .Where(worker => worker.ProfileKey == profileKey
                        && worker.EnvClass == effectiveEnvClass
                        && !worker.IsBusy
                        && clock.GetUtcNow() - worker.LastActiveAt > settings.IdleTtl)
                    .OrderBy(worker => worker.LastActiveAt)
                    .Take(decision.StopIdleWorkers);
                foreach (var worker in staleWorkers)
                {
                    await computeProvider.StopAsync(worker.Id, ComputeStopReason.IdleTtl, cancellationToken);
                    tokenIssuer.Revoke(worker.TokenId);
                    pool.Remove(worker.Id);
                    logger.LogInformation(
                        "Scale supervisor stopped idle worker {WorkerId} of project {ProjectId} profile {ProfileKey} class {EnvClass} after idle TTL {IdleTtl}",
                        worker.Id.Value,
                        projectId.Value,
                        profileKey,
                        effectiveEnvClass,
                        settings.IdleTtl);
                }
            }
        }
    }
}
