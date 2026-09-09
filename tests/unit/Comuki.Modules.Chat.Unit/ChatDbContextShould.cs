using Comuki.Modules.Chat.Domain.Sessions;
using Comuki.Modules.Chat.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Subject-scope query filter behaviour on
/// <see cref="ChatDbContext"/>: rows in <c>chat_sessions</c> are
/// visible only when they have no project (cross-project, visible to
/// every subject) or when their project matches the ambient scope. The
/// test uses EF Core's InMemory provider — the production runtime sits on
/// Npgsql + snake_case, but the query filter itself is provider-agnostic.
/// </summary>
public sealed class ChatDbContextShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a context built without a scope accessor, when sessions of two distinct projects are read, then both surface (the system-context fallback sees every row)")]
    public async Task SystemContextSeesAllSessionsAsync()
    {
        var context = NewSystemContext();

        context.Sessions.Add(ChatSession.Create(ProjectId.New(), Guid.CreateVersion7(), "session-a", anchorTime));
        context.Sessions.Add(ChatSession.Create(ProjectId.New(), Guid.CreateVersion7(), "session-b", anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Sessions.ToListAsync(TestContext.Current.CancellationToken);

        visible.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a restricted scope that does not include a session's project, when sessions are read, then the subject-scope query filter hides the row")]
    public async Task RestrictedScopeHidesOutOfScopeSessionsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [ProjectId.New()]);
        var context = NewScopedContext(accessor);

        context.Sessions.Add(ChatSession.Create(ProjectId.New(), Guid.CreateVersion7(), "out-of-scope", anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Sessions.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a restricted scope that DOES include a session's project, when sessions are read, then the subject-scope query filter surfaces it")]
    public async Task RestrictedScopeSurfacesInScopeSessionsAsync()
    {
        var inScopeProject = ProjectId.New();
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [inScopeProject]);
        var context = NewScopedContext(accessor);

        context.Sessions.Add(ChatSession.Create(inScopeProject, Guid.CreateVersion7(), "in-scope", anchorTime));
        context.Sessions.Add(ChatSession.Create(ProjectId.New(), Guid.CreateVersion7(), "out-of-scope", anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Sessions.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
        visible[0].ProjectId.ShouldBe(inScopeProject);
    }

    [Fact(DisplayName = "Given cross-project sessions (null ProjectId), when a restricted subject reads, then they are still visible (the global-corpus semantics)")]
    public async Task CrossProjectSessionsAreVisibleToAllSubjectsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: []);
        var context = NewScopedContext(accessor);

        context.Sessions.Add(ChatSession.Create(null, Guid.CreateVersion7(), "global-session", anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Sessions.ToListAsync(TestContext.Current.CancellationToken);

        visible.ShouldHaveSingleItem();
    }

    private static ChatDbContext NewSystemContext()
    {
        return NewContext(scopeAccessor: null);
    }

    private static ChatDbContext NewScopedContext(ISubjectScopeAccessor accessor)
    {
        return NewContext(accessor);
    }

    private static ChatDbContext NewContext(ISubjectScopeAccessor? scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: $"chat-ctx-{Guid.NewGuid():N}")
            .Options;
        return new ChatDbContext(options, scopeAccessor);
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
