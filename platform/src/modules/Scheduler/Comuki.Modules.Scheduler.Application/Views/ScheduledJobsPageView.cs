namespace Comuki.Modules.Scheduler.Application.Views;

/// <summary>
/// One paginated read of scheduled jobs for a project (application-layer
/// view). The host maps this to its wire-format
/// <c>ScheduledJobsPage</c>; the application layer does not depend on the
/// host type so the page shape lives here.
/// </summary>
/// <param name="Items">Page contents (already-projected views).</param>
/// <param name="Total">Total jobs of the project, regardless of pagination.</param>
public sealed record ScheduledJobsPageView(IReadOnlyList<ScheduledJobView> Items, int Total);
