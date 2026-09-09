using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// Subject-scope query filter behaviour on
/// <see cref="SchedulerDbContext"/>: rows in <c>scheduled_jobs</c> are
/// visible only when their project matches the ambient scope (system /
/// unrestricted sees everything; restricted subjects see only their
/// projects). The test uses EF Core's InMemory provider — the production
/// runtime sits on Npgsql + snake_case, but the query filter itself is
/// provider-agnostic.
/// </summary>
public sealed class SchedulerDbContextShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a context built without a scope accessor, when scheduled jobs of two distinct projects are read, then both surface (the system-context fallback sees every row)")]
    public async Task SystemContextSeesAllJobsAsync()
    {
        var context = NewSystemContext();

        var firstProject = ProjectId.New();
        var secondProject = ProjectId.New();
        context.ScheduledJobs.Add(ScheduledJob.Create(firstProject, "*/5 * * * *", "default", "{}", null, true, anchorTime));
        context.ScheduledJobs.Add(ScheduledJob.Create(secondProject, "*/10 * * * *", "default", "{}", null, true, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.ScheduledJobs.ToListAsync(TestContext.Current.CancellationToken);

        visible.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a context with a restricted scope that does not include the job's project, when scheduled jobs are read, then the subject-scope query filter hides the row")]
    public async Task RestrictedScopeHidesOutOfScopeJobsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [ProjectId.New()]);
        var context = NewScopedContext(accessor);

        context.ScheduledJobs.Add(ScheduledJob.Create(ProjectId.New(), "*/5 * * * *", "default", "{}", null, true, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.ScheduledJobs.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a context with a restricted scope that DOES include the job's project, when scheduled jobs are read, then the subject-scope query filter surfaces it")]
    public async Task RestrictedScopeSurfacesInScopeJobsAsync()
    {
        var inScopeProject = ProjectId.New();
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [inScopeProject]);
        var context = NewScopedContext(accessor);

        context.ScheduledJobs.Add(ScheduledJob.Create(inScopeProject, "*/5 * * * *", "default", "{}", null, true, anchorTime));
        context.ScheduledJobs.Add(ScheduledJob.Create(ProjectId.New(), "*/5 * * * *", "default", "{}", null, true, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.ScheduledJobs.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
        visible[0].ProjectId.ShouldBe(inScopeProject);
    }

    private static SchedulerDbContext NewSystemContext()
    {
        return NewContext(scopeAccessor: null);
    }

    private static SchedulerDbContext NewScopedContext(ISubjectScopeAccessor accessor)
    {
        return NewContext(accessor);
    }

    private static SchedulerDbContext NewContext(ISubjectScopeAccessor? scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseInMemoryDatabase(databaseName: $"sched-ctx-{Guid.NewGuid():N}")
            .Options;
        return new SchedulerDbContext(options, scopeAccessor);
    }

    private static ISubjectScopeAccessor NewScopeAccessor(bool unrestricted, IReadOnlyList<ProjectId> projectIds)
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(unrestricted, SystemName: unrestricted ? "unit-test" : null, projectIds));
        accessor.CurrentOrNone.Returns(accessor.Current);
        accessor.AsSystem(Arg.Any<string>()).Returns(static _ => new NoopHandle());
        accessor.Begin(Arg.Any<SubjectScope>()).Returns(static _ => new NoopHandle());
        return accessor;
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
