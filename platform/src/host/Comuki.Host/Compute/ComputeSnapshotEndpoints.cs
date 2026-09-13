using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Compute;

/// <summary>
/// Wire shape of <c>GET /api/v1/compute</c> — a read-only snapshot of what
/// the host can honestly observe: the configured provider, the scale
/// defaults from <c>Compute:Scale</c>, and per-profile pool counts derived
/// from the work-item queue. Capacity (<c>freeSlots</c>) and recent spawns
/// are omitted on purpose — they need a composed compute provider, which
/// this host does not wire.
/// </summary>
/// <param name="Provider">Configured provider key: <c>docker</c> or <c>kubernetes</c>.</param>
/// <param name="Defaults">Scale defaults every project uses until a per-project override lands.</param>
/// <param name="Pools">Per-project × per-profile counts.</param>
public sealed record ComputeSnapshotView(
    string Provider,
    ComputeScaleDefaultsView Defaults,
    IReadOnlyList<ComputePoolView> Pools);

/// <summary>Scale defaults from <see cref="ScaleSupervisorOptions"/>.</summary>
/// <param name="WorkerImage">Default worker image (digest-pinned in production).</param>
/// <param name="ProfilesGitRef">Default pinned profiles git ref.</param>
/// <param name="MinIdle">Warm-idle floor per profile.</param>
/// <param name="MaxConcurrent">Cap on concurrently running workers per project.</param>
/// <param name="IdleTtlSeconds">Idle TTL before a worker is a reaper candidate, in seconds.</param>
/// <param name="PollIntervalSeconds">Delay between supervisor passes, in seconds.</param>
/// <param name="ProfileKeys">Profile keys the supervisor polls per project.</param>
public sealed record ComputeScaleDefaultsView(
    string WorkerImage,
    string ProfilesGitRef,
    int MinIdle,
    int MaxConcurrent,
    long IdleTtlSeconds,
    long PollIntervalSeconds,
    IReadOnlyList<string> ProfileKeys);

/// <summary>One project × profile pool with the counts the queue can answer.</summary>
/// <param name="ProjectId">Owning project.</param>
/// <param name="ProfileKey">Profile the pool serves.</param>
/// <param name="Queued">Items waiting for a claim.</param>
/// <param name="Running">Items currently leased (busy workers).</param>
/// <param name="MinIdle">Project's warm-idle floor (projects settings override the default).</param>
/// <param name="MaxConcurrent">Project's concurrency cap.</param>
public sealed record ComputePoolView(
    Guid ProjectId,
    string ProfileKey,
    int Queued,
    int Running,
    int MinIdle,
    int MaxConcurrent);

/// <summary>
/// Read-only compute snapshot endpoint (permission <c>queue:read</c>): the
/// configured provider + scale defaults, plus per-project × per-profile
/// queued/running counts joined with the project's scale settings.
/// </summary>
public static class ComputeSnapshotEndpoints
{
    /// <summary>Maps the compute snapshot endpoint.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapComputeSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Compute, GetSnapshotAsync).WithTags("Compute");
        return app;
    }

    [RequiresPermission("queue:read")]
    private static async Task<IResult> GetSnapshotAsync(
        IOptions<ComputeOptions> computeOptions,
        IOptions<ScaleSupervisorOptions> scaleOptions,
        IProjectScaleSettings projectScale,
        OrchestrationDbContext db,
        CancellationToken cancellationToken)
    {
        var scale = scaleOptions.Value;

        var perProfile = await db.WorkItems.AsNoTracking()
            .Where(item => item.Status == WorkItemStatus.Queued || item.Status == WorkItemStatus.Running)
            .GroupBy(item => new { item.RunId, item.ProfileKey, item.Status })
            .Select(static group => new { group.Key.RunId, group.Key.ProfileKey, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var runIds = perProfile.Select(static row => row.RunId).Distinct().ToList();
        var projectByRunId = await db.Runs.AsNoTracking()
            .Where(run => runIds.Contains(run.Id))
            .ToDictionaryAsync(run => run.Id, run => run.ProjectId, cancellationToken);

        var pools = perProfile
            .GroupBy(row => (ProjectId?)projectByRunId.GetValueOrDefault(row.RunId))
            .Where(group => group.Key is not null)
            .SelectMany(project => project
                .GroupBy(row => row.ProfileKey)
                .Select(profile => new
                {
                    ProjectId = project.Key!.Value,
                    ProfileKey = profile.Key,
                    Queued = profile.Sum(row => row.Status == WorkItemStatus.Queued ? row.Count : 0),
                    Running = profile.Sum(row => row.Status == WorkItemStatus.Running ? row.Count : 0),
                }))
            .OrderBy(pool => pool.ProjectId.Value)
            .ThenBy(pool => pool.ProfileKey)
            .Select(pool =>
            {
                var settings = projectScale.Get(pool.ProjectId);
                return new ComputePoolView(
                    pool.ProjectId.Value,
                    pool.ProfileKey,
                    pool.Queued,
                    pool.Running,
                    settings.MinIdle,
                    settings.MaxConcurrent);
            })
            .ToList();

        var snapshot = new ComputeSnapshotView(
            computeOptions.Value.Provider,
            new ComputeScaleDefaultsView(
                scale.WorkerImage,
                scale.ProfilesGitRef,
                scale.MinIdle,
                scale.MaxConcurrent,
                (long)scale.IdleTtl.TotalSeconds,
                (long)scale.PollInterval.TotalSeconds,
                scale.ProfileKeys),
            pools);

        return Results.Ok(snapshot);
    }
}
