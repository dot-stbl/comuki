using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Filtering;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs;

/// <summary>
/// Read-side query behind <c>GET /api/v1/runs</c>: paged run listing with the
/// filter/sort DSL. The scope axis is enforced by the orchestration context's
/// global query filter — out-of-scope rows never reach the page, and the
/// total count matches what the subject can see. Lives in the host (not the
/// engine) because only the composition root may reference orchestration
/// internals.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
public sealed class RunsListHandler(OrchestrationDbContext db)
{
    /// <summary>
    /// Default ordering when the request carries no sort spec: newest activity
    /// first. (The DSL fallback would order by the first filterable field —
    /// Status — which is not a useful default for a runs list.)
    /// </summary>
    public const string DefaultSort = "UpdatedAt,desc";

    /// <summary>Applies filter + sort + paging and returns the page with the filtered total.</summary>
    /// <param name="query">Normalized query envelope.</param>
    /// <param name="cancellationToken"></param>
    public async Task<RunsPage> ListAsync(FilterQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var filtered = db.Runs.AsNoTracking()
            .ApplyFilter(normalized.Filter);

        var total = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .ApplySort(string.IsNullOrWhiteSpace(normalized.Sort) ? DefaultSort : normalized.Sort)
            .Skip(normalized.Skip())
            .Take(normalized.PageSize)
            .Select(static run => new RunView(
                run.Id.Value,
                run.ProjectId.Value,
                run.Status.ToString().ToLowerInvariant(),
                run.CreatedAt,
                run.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new RunsPage(items, normalized.Page, normalized.PageSize, total);
    }
}

/// <summary>Wire row of one run — mirrors the RunSummary contract fields.</summary>
/// <param name="Id">Run id.</param>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="Status">Run status wire string (queued, running, …).</param>
/// <param name="CreatedAt">When the run was admitted.</param>
/// <param name="UpdatedAt">Last status change.</param>
public sealed record RunView(
    Guid Id,
    Guid ProjectId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One page of runs plus the paging envelope.</summary>
/// <param name="Items">Page rows.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Total">Total rows matching the filter (subject-visible).</param>
public sealed record RunsPage(
    IReadOnlyList<RunView> Items,
    int Page,
    int PageSize,
    int Total);
