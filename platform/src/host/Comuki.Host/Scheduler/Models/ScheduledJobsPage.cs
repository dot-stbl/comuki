using Comuki.Modules.Scheduler.Application.Views;

namespace Comuki.Host.Scheduler.Models;

/// <summary>
/// One paginated read of scheduled jobs for a project.
/// Wire shape: <c>{ "items": [ScheduledJobView], "total": N }</c>.
/// The scheduler surface is small (per project, one page per request)
/// — pagination is opt-in via <c>?page</c> + <c>?pageSize</c>; the
/// default returns everything.
/// </summary>
/// <param name="Items">Page contents (already-projected views).</param>
/// <param name="Total">Total jobs in the project (not the page size).</param>
public sealed record ScheduledJobsPage(IReadOnlyList<ScheduledJobView> Items, int Total);
