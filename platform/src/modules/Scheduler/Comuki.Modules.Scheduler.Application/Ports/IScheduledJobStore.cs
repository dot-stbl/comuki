using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Scheduler.Application.Ports;

/// <summary>
/// The scheduler persistence port. Mirrors the intake store shape: every
/// CRUD verb plus the due-job query the dispatcher polls against. The
/// host never reaches into the scheduler schema — implementations live in
/// <c>Comuki.Modules.Scheduler.Infrastructure</c>.
/// </summary>
public interface IScheduledJobStore
{
    /// <summary>Inserts a new job.</summary>
    /// <param name="job"></param>
    /// <param name="cancellationToken"></param>
    public Task AddAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>Lookup by id.</summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken"></param>
    public Task<ScheduledJob?> FindAsync(ScheduledJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Lists all jobs of a project, newest first.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ScheduledJob>> ListAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a page of jobs of a project (newest first) plus the project's
    /// total count. The <c>Skip</c> / <c>Take</c> are pushed to SQL by EF
    /// Core — at most <paramref name="pageSize"/> rows are read from the
    /// store, regardless of how large the project is.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="page">1-based page index; values &lt; 1 are clamped to 1.</param>
    /// <param name="pageSize">Rows per page; clamped to <c>[1, 500]</c>.</param>
    /// <param name="cancellationToken"></param>
    public Task<ScheduledJobPage> ListPagedAsync(
        ProjectId projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated job.</summary>
    /// <param name="job"></param>
    /// <param name="cancellationToken"></param>
    public Task UpdateAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>Deletes a job; missing ids are a no-op.</summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken"></param>
    public Task DeleteAsync(ScheduledJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns due, enabled jobs whose <c>next_fire_at &lt;= now</c> are
    /// claimed with <c>FOR UPDATE SKIP LOCKED</c> in the implementation —
    /// two host replicas cannot both fire the same job on the same tick.
    /// The returned jobs are NOT pre-mutated: the dispatcher computes
    /// <c>lastFiredAt</c> + <c>nextFireAt</c> and persists them in the
    /// same scope.
    /// </summary>
    /// <param name="now"></param>
    /// <param name="limit"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<ScheduledJob>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);
}
