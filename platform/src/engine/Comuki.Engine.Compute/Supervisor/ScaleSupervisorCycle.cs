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
/// One scale supervisor pass (issue #3 T2.4/T2.5): reconcile the worker pool
/// with the provider, read the backlog per profile, apply the pure scale
/// policy, then start workers (token via <see cref="WorkerTokenIssuer"/>,
/// image/ref labels from settings+options) and reap stale idle ones with
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
    /// <param name="cancellationToken"></param>
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
            foreach (var profileKey in options.ProfileKeys)
            {
                var queuedCount = await backlogReader.CountQueuedAsync(projectId, profileKey, cancellationToken);
                var projectWorkers = pool.List(projectId);
                var profileWorkers = projectWorkers.Where(worker => worker.ProfileKey == profileKey).ToArray();
                var idleCount = profileWorkers.Count(worker => !worker.IsBusy);
                var staleIdleCount = profileWorkers.Count(
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
                        "Scale decision for project {ProjectId} profile {ProfileKey} clamped by provider capacity: freeSlots={FreeSlots}",
                        projectId.Value,
                        profileKey,
                        remainingFreeSlots);
                }

                logger.LogInformation(
                    "Scale decision for project {ProjectId} profile {ProfileKey}: queued={QueuedCount} idle={IdleCount} staleIdle={StaleIdleCount} running={RunningCount} freeSlots={FreeSlots}; start={StartWorkers} stopIdle={StopIdleWorkers} clampedByCapacity={ClampedByCapacity}",
                    projectId.Value,
                    profileKey,
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
                        Image = settings.WorkerImage ?? options.WorkerImage,
                        WorkerToken = tokenIssuer.Issue(tokenId),
                        OrchestratorGrpcUrl = options.OrchestratorGrpcUrl,
                    };

                    var handle = await computeProvider.StartAsync(request, cancellationToken);
                    pool.Register(handle, tokenId, projectId, profileKey);
                    remainingFreeSlots = Math.Max(0, remainingFreeSlots - 1);
                    logger.LogInformation(
                        "Scale supervisor started worker {WorkerId} for project {ProjectId} profile {ProfileKey}",
                        handle.Id.Value,
                        projectId.Value,
                        profileKey);
                }

                if (decision.StopIdleWorkers is 0)
                {
                    continue;
                }

                var staleWorkers = pool.List(projectId)
                    .Where(worker => worker.ProfileKey == profileKey
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
                        "Scale supervisor stopped idle worker {WorkerId} of project {ProjectId} profile {ProfileKey} after idle TTL {IdleTtl}",
                        worker.Id.Value,
                        projectId.Value,
                        profileKey,
                        settings.IdleTtl);
                }
            }
        }
    }
}
