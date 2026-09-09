using Comuki.Modules.Scheduler.Domain.Jobs;

namespace Comuki.Modules.Scheduler.Application.Ports;

/// <summary>
/// One page of scheduled jobs for a project — the store's pagination
/// return shape. The host's wire-format <c>ScheduledJobsPage</c> wraps
/// the views that this page's jobs are mapped into; this stays in the
/// Scheduler module so the store / service do not depend on a host type.
/// </summary>
/// <param name="Items">Jobs on this page (already project-scoped + ordered newest first).</param>
/// <param name="Total">Total jobs of the project, regardless of pagination.</param>
public sealed record ScheduledJobPage(IReadOnlyList<ScheduledJob> Items, int Total);
