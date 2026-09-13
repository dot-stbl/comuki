using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Leak 2 (memory has no row-level scoping at all): proves
/// <see cref="MemoryDbContext"/>'s new <c>HasQueryFilter</c> on
/// <see cref="MemoryFact"/> is what stops a restricted caller from
/// crossing a project or subject boundary. Before this filter existed
/// every query below returned the other project's/subject's fact,
/// because nothing checked the ambient scope at all — mirrors
/// <c>Comuki.Engine.Orchestration.Unit.DbContext.OrchestrationDbContextShould</c>,
/// the precedent this filter was modeled on.
/// </summary>
public sealed class MemoryDbContextScopeShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given project-scoped facts of two projects, when a caller restricted to one project reads, then only its own project's fact surfaces")]
    public async Task RestrictedScopeHidesOtherProjectsFactsAsync()
    {
        var ownProject = ProjectId.New();
        var otherProject = ProjectId.New();
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [ownProject]);
        var context = NewContext(accessor);

        context.MemoryFacts.Add(ProjectFact(ownProject, "roadmap", "own project plan"));
        context.MemoryFacts.Add(ProjectFact(otherProject, "roadmap", "other project's plan — must stay invisible"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem().Text.ShouldBe("own project plan");
    }

    [Fact(DisplayName = "Given the same two projects, when an unrestricted (system) caller reads, then both projects' facts surface")]
    public async Task UnrestrictedScopeSeesEveryProjectAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        using var scope = accessor.AsSystem("unit-test");
        var context = NewContext(accessor);

        context.MemoryFacts.Add(ProjectFact(ProjectId.New(), "roadmap", "project A plan"));
        context.MemoryFacts.Add(ProjectFact(ProjectId.New(), "roadmap", "project B plan"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a global fact, when a restricted caller with no project assignments at all reads, then the global fact still surfaces")]
    public async Task RestrictedScopeStillSeesGlobalFactsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: []);
        var context = NewContext(accessor);

        context.MemoryFacts.Add(GlobalFact("policy", "everyone can see this"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem().Text.ShouldBe("everyone can see this");
    }

    [Fact(DisplayName = "Given a user-scoped fact, when a restricted caller reads, then it is hidden — fail-closed, because SubjectScope has no per-user identity axis to check against")]
    public async Task RestrictedScopeHidesUserScopedFactsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: []);
        var context = NewContext(accessor);

        context.MemoryFacts.Add(UserFact("victim-user", "prefers", "dark mode"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given the same user-scoped fact, when an unrestricted (system) caller reads, then it surfaces")]
    public async Task UnrestrictedScopeSeesUserScopedFactsAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        using var scope = accessor.AsSystem("unit-test");
        var context = NewContext(accessor);

        context.MemoryFacts.Add(UserFact("victim-user", "prefers", "dark mode"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem().Text.ShouldBe("dark mode");
    }

    [Fact(DisplayName = "Given a context built without a scope accessor at all, when facts are saved and re-read, then every one of them is visible (the system-context fallback sees everything)")]
    public async Task NoAccessorDefaultsUnrestrictedAsync()
    {
        var context = NewContext(scopeAccessor: null);

        context.MemoryFacts.Add(ProjectFact(ProjectId.New(), "roadmap", "some project plan"));
        context.MemoryFacts.Add(UserFact("some-user", "prefers", "light mode"));
        context.MemoryFacts.Add(GlobalFact("policy", "global"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.MemoryFacts.ToListAsync(TestContext.Current.CancellationToken);

        visible.Count.ShouldBe(3);
    }

    private static MemoryFact ProjectFact(ProjectId projectId, string topicKey, string text)
    {
        return MemoryFact.Create(
            MemoryScope.Project, projectId.Value.ToString(), MemoryFactKind.Standing, topicKey, text,
            MemorySource.Chat, "tester", now);
    }

    private static MemoryFact GlobalFact(string topicKey, string text)
    {
        return MemoryFact.Create(
            MemoryScope.Global, MemoryScopeKeys.GlobalSubject, MemoryFactKind.Standing, topicKey, text,
            MemorySource.Chat, "tester", now);
    }

    private static MemoryFact UserFact(string subjectId, string topicKey, string text)
    {
        return MemoryFact.Create(
            MemoryScope.User, subjectId, MemoryFactKind.Standing, topicKey, text,
            MemorySource.Chat, subjectId, now);
    }

    private static MemoryDbContext NewContext(ISubjectScopeAccessor? scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase(databaseName: $"memory-scope-{Guid.NewGuid():N}")
            .Options;
        return new MemoryDbContext(options, scopeAccessor);
    }

    private static ISubjectScopeAccessor NewScopeAccessor(bool unrestricted, IReadOnlyList<ProjectId> projectIds)
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(unrestricted, SystemName: unrestricted ? "unit-test" : null, projectIds));
        accessor.CurrentOrNone.Returns(accessor.Current);
        return accessor;
    }
}
