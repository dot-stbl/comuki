using Comuki.Modules.Costs.Domain.Events;
using Comuki.Modules.Costs.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>
/// Subject-scope query filter behaviour on
/// <see cref="CostsDbContext"/>: rows in <c>usage_events</c> are visible
/// only when their project matches the ambient scope (system / unrestricted
/// sees everything; restricted subjects see only their projects). The test
/// uses EF Core's InMemory provider against a pristine database — the
/// production runtime sits on Npgsql + snake_case, but the query filter
/// itself is provider-agnostic.
/// </summary>
public sealed class CostsDbContextShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a context built without a scope accessor, when usage events of two distinct projects are read, then both surface (the system-context fallback sees every row)")]
    public async Task SystemContextSeesAllRowsAsync()
    {
        var context = NewSystemContext();

        var firstProject = ProjectId.New();
        var secondProject = ProjectId.New();
        context.UsageEvents.Add(UsageEvent.Create(firstProject, null, UsageSource.Proxy, "model-a", 1, 1, 10, anchorTime));
        context.UsageEvents.Add(UsageEvent.Create(secondProject, null, UsageSource.Brain, "model-b", 1, 1, 20, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.UsageEvents.ToListAsync(TestContext.Current.CancellationToken);

        visible.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a context with a restricted scope that does not include the event's project, when usage events are read, then the subject-scope query filter hides the row")]
    public async Task RestrictedScopeHidesOutOfScopeEventsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [ProjectId.New()]);
        var context = NewScopedContext(accessor);

        var outOfScope = ProjectId.New();
        context.UsageEvents.Add(UsageEvent.Create(outOfScope, null, UsageSource.Proxy, "model", 1, 1, 10, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.UsageEvents.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a context with a restricted scope that DOES include the event's project, when usage events are read, then the subject-scope query filter surfaces it")]
    public async Task RestrictedScopeSurfacesInScopeEventsAsync()
    {
        var inScopeProject = ProjectId.New();
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [inScopeProject]);
        var context = NewScopedContext(accessor);

        context.UsageEvents.Add(UsageEvent.Create(inScopeProject, null, UsageSource.Proxy, "model", 1, 1, 10, anchorTime));
        context.UsageEvents.Add(UsageEvent.Create(ProjectId.New(), null, UsageSource.Proxy, "model", 1, 1, 10, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.UsageEvents.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
        visible[0].ProjectId.ShouldBe(inScopeProject);
    }

    private static CostsDbContext NewSystemContext()
    {
        return NewContext(scopeAccessor: null);
    }

    private static CostsDbContext NewScopedContext(ISubjectScopeAccessor accessor)
    {
        return NewContext(accessor);
    }

    private static CostsDbContext NewContext(ISubjectScopeAccessor? scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<CostsDbContext>()
            .UseInMemoryDatabase(databaseName: $"costs-ctx-{Guid.NewGuid():N}")
            .Options;
        return new CostsDbContext(options, scopeAccessor);
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
