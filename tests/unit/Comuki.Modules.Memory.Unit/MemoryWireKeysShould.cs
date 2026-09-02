using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Wire-key tables used by EF converters and the brain/chat surface: Key /
/// Parse / ParseRequired round-trips, unknown keys, and out-of-range enums.
/// </summary>
public sealed class MemoryWireKeysShould
{
    [Theory(DisplayName = "Given a memory fact kind, when Key then Parse, then the kind round-trips")]
    [InlineData(MemoryFactKind.Standing, MemoryFactKindKeys.Standing)]
    [InlineData(MemoryFactKind.Ephemeral, MemoryFactKindKeys.Ephemeral)]
    public void RoundTripFactKind(MemoryFactKind kind, string key)
    {
        MemoryFactKindKeys.Key(kind).ShouldBe(key);
        MemoryFactKindKeys.Parse(key).ShouldBe(kind);
        MemoryFactKindKeys.ParseRequired(key).ShouldBe(kind);
    }

    [Fact(DisplayName = "Given an unknown fact kind key, when Parse is called, then null; ParseRequired throws")]
    public void RefuseUnknownFactKind()
    {
        MemoryFactKindKeys.Parse("nope").ShouldBeNull();
        Should.Throw<InvalidOperationException>(static () => MemoryFactKindKeys.ParseRequired("nope"))
            .Message.ShouldContain("unknown memory fact kind");
        Should.Throw<ArgumentOutOfRangeException>(static () => MemoryFactKindKeys.Key((MemoryFactKind)99));
    }

    [Theory(DisplayName = "Given a memory scope, when Key then Parse, then the scope round-trips")]
    [InlineData(MemoryScope.User, MemoryScopeKeys.User)]
    [InlineData(MemoryScope.Project, MemoryScopeKeys.Project)]
    [InlineData(MemoryScope.Global, MemoryScopeKeys.Global)]
    public void RoundTripScope(MemoryScope scope, string key)
    {
        MemoryScopeKeys.Key(scope).ShouldBe(key);
        MemoryScopeKeys.Parse(key).ShouldBe(scope);
        MemoryScopeKeys.ParseRequired(key).ShouldBe(scope);
    }

    [Fact(DisplayName = "Given an unknown scope key, when Parse is called, then null; ParseRequired throws")]
    public void RefuseUnknownScope()
    {
        MemoryScopeKeys.GlobalSubject.ShouldBe("global");
        MemoryScopeKeys.Parse("team").ShouldBeNull();
        Should.Throw<InvalidOperationException>(static () => MemoryScopeKeys.ParseRequired("team"))
            .Message.ShouldContain("unknown memory scope");
        Should.Throw<ArgumentOutOfRangeException>(static () => MemoryScopeKeys.Key((MemoryScope)42));
    }

    [Theory(DisplayName = "Given a memory source, when Key then Parse, then the source round-trips")]
    [InlineData(MemorySource.Chat, MemorySourceKeys.Chat)]
    [InlineData(MemorySource.Human, MemorySourceKeys.Human)]
    [InlineData(MemorySource.Run, MemorySourceKeys.Run)]
    [InlineData(MemorySource.LearningApproved, MemorySourceKeys.LearningApproved)]
    public void RoundTripSource(MemorySource source, string key)
    {
        MemorySourceKeys.Key(source).ShouldBe(key);
        MemorySourceKeys.Parse(key).ShouldBe(source);
        MemorySourceKeys.ParseRequired(key).ShouldBe(source);
    }

    [Fact(DisplayName = "Given an unknown source key, when Parse is called, then null; ParseRequired throws")]
    public void RefuseUnknownSource()
    {
        MemorySourceKeys.Parse("webhook").ShouldBeNull();
        Should.Throw<InvalidOperationException>(static () => MemorySourceKeys.ParseRequired("webhook"))
            .Message.ShouldContain("unknown memory source");
        Should.Throw<ArgumentOutOfRangeException>(static () => MemorySourceKeys.Key((MemorySource)7));
    }

    [Theory(DisplayName = "Given a learning status, when Key then Parse, then the status round-trips")]
    [InlineData(LearningStatus.Pending, LearningStatusKeys.Pending)]
    [InlineData(LearningStatus.Approved, LearningStatusKeys.Approved)]
    [InlineData(LearningStatus.Rejected, LearningStatusKeys.Rejected)]
    public void RoundTripLearningStatus(LearningStatus status, string key)
    {
        LearningStatusKeys.Key(status).ShouldBe(key);
        LearningStatusKeys.Parse(key).ShouldBe(status);
        LearningStatusKeys.ParseRequired(key).ShouldBe(status);
    }

    [Fact(DisplayName = "Given an unknown learning status key, when Parse is called, then null; ParseRequired throws")]
    public void RefuseUnknownLearningStatus()
    {
        LearningStatusKeys.Parse("deferred").ShouldBeNull();
        Should.Throw<InvalidOperationException>(static () => LearningStatusKeys.ParseRequired("deferred"))
            .Message.ShouldContain("unknown learning status");
        Should.Throw<ArgumentOutOfRangeException>(static () => LearningStatusKeys.Key((LearningStatus)9));
    }

    [Fact(DisplayName = "Given a fresh MemoryFactId, when New is called, then the value is version-7 and ToString matches")]
    public void MintMemoryFactId()
    {
        var id = MemoryFactId.New();

        id.Value.Version.ShouldBe(7);
        id.ToString().ShouldBe(id.Value.ToString());
    }
}
