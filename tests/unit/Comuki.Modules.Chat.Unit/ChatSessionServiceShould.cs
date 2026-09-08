using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Sessions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Session lifecycle: the create / find-owned / list-recent path over the
/// shared in-memory <see cref="FakeChatSessionStore"/>. The Active-session
/// filter on <c>FindOwnedAsync</c> is the contract under test — a foreign
/// subject, a missing session, or an archived session must all read as null.
/// </summary>
public sealed class ChatSessionServiceShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given no title, when CreateAsync is called, then the session title defaults to 'New chat'")]
    public async Task CreateAsyncUsesDefaultTitleWhenTitleIsNullAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var subjectId = Guid.NewGuid();
        var projectId = Shared.Kernel.Ids.ProjectId.New();

        var session = await service.CreateAsync(subjectId, projectId, title: null, TestContext.Current.CancellationToken);

        session.Title.ShouldBe("New chat");
        session.SubjectId.ShouldBe(subjectId);
        session.ProjectId.ShouldBe(projectId);
        session.Status.ShouldBe(ChatSessionStatus.Active);
        session.CreatedAt.ShouldBe(anchorTime);
        session.UpdatedAt.ShouldBe(anchorTime);
        store.Messages.ShouldBeEmpty();
        store.NewSessionCount().ShouldBe(1);
    }

    [Fact(DisplayName = "Given an explicit title, when CreateAsync is called, then the session carries the trimmed title")]
    public async Task CreateAsyncPreservesExplicitTitleAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var subjectId = Guid.NewGuid();

        var session = await service.CreateAsync(subjectId, projectId: null, title: "  sprint retro  ", TestContext.Current.CancellationToken);

        session.Title.ShouldBe("sprint retro");
        store.NewSessionCount().ShouldBe(1);
    }

    [Fact(DisplayName = "Given a session that belongs to a different subject, when FindOwnedAsync is called, then null is returned")]
    public async Task FindOwnedAsyncReturnsNullForForeignSubjectAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var session = ChatSession.Create(projectId: null, owner, title: "owned", anchorTime);
        store.Seed(session);

        var result = await service.FindOwnedAsync(session.Id, intruder, TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an active session that belongs to the caller, when FindOwnedAsync is called, then the session is returned")]
    public async Task FindOwnedAsyncReturnsSessionForOwnerAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var subjectId = Guid.NewGuid();
        var session = ChatSession.Create(projectId: null, subjectId, title: "mine", anchorTime);
        store.Seed(session);

        var result = await service.FindOwnedAsync(session.Id, subjectId, TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(session.Id);
    }

    [Fact(DisplayName = "Given an archived session owned by the caller, when FindOwnedAsync is called, then null is returned")]
    public async Task FindOwnedAsyncReturnsNullForArchivedSessionAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var subjectId = Guid.NewGuid();
        var session = ChatSession.Create(projectId: null, subjectId, title: "stale", anchorTime);
        session.Archive(anchorTime.AddDays(31));
        store.Seed(session);

        var result = await service.FindOwnedAsync(session.Id, subjectId, TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the subject has recent sessions, when ListRecentAsync is called, then the store's most-recent list is returned with the default cap")]
    public async Task ListRecentAsyncDelegatesToStoreAsync()
    {
        var store = new FakeChatSessionStore();
        var service = NewService(store);
        var subjectId = Guid.NewGuid();
        var first = ChatSession.Create(projectId: null, subjectId, "a", anchorTime.AddMinutes(-5));
        var second = ChatSession.Create(projectId: null, subjectId, "b", anchorTime.AddMinutes(-3));
        var third = ChatSession.Create(projectId: null, subjectId, "c", anchorTime.AddMinutes(-1));
        store.Seed(first);
        store.Seed(second);
        store.Seed(third);

        var result = await service.ListRecentAsync(subjectId, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(3);
        result[0].Id.ShouldBe(third.Id);
        result[1].Id.ShouldBe(second.Id);
        result[2].Id.ShouldBe(first.Id);
    }

    private static ChatSessionService NewService(FakeChatSessionStore store)
    {
        return new ChatSessionService(store, new FixedTimeProvider(anchorTime));
    }
}

/// <summary>Deterministic <see cref="TimeProvider"/> for chat session tests.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly DateTimeOffset now = now;

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
