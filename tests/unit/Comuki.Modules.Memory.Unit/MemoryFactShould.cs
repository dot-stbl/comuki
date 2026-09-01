using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Factory and supersede semantics of the fact entity: canonicalization of
/// subject/topic, argument guards, and the supersede transition the store
/// relies on for its one-active-row-per-topic contract.
/// </summary>
public sealed class MemoryFactShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given messy subject and topic, when Create is called, then both are canonicalized")]
    public void CanonicalizeSubjectAndTopicOnCreate()
    {
        var fact = MemoryFact.Create(
            MemoryScope.User,
            "  User-42  ",
            MemoryFactKind.Standing,
            "  Deploy-Prefers-Docker  ",
            "  prefers docker compose deploys  ",
            MemorySource.Chat,
            "user-42",
            now);

        fact.SubjectId.ShouldBe("user-42");
        fact.TopicKey.ShouldBe("deploy-prefers-docker");
        fact.Text.ShouldBe("prefers docker compose deploys");
        fact.CreatedBy.ShouldBe("user-42");
        fact.CreatedAt.ShouldBe(now);
        fact.SupersededAt.ShouldBeNull();
    }

    [Theory(DisplayName = "Given an empty field, when Create is called, then ArgumentException names it")]
    [InlineData("subjectId")]
    [InlineData("topicKey")]
    [InlineData("text")]
    public void RefuseEmptyFields(string paramName)
    {
        var exception = Should.Throw<ArgumentException>(() => MemoryFact.Create(
            MemoryScope.User,
            paramName == "subjectId" ? "  " : "user-42",
            MemoryFactKind.Standing,
            paramName == "topicKey" ? "" : "deploy",
            paramName == "text" ? "   " : "prefers docker",
            MemorySource.Chat,
            "user-42",
            now));

        exception.ParamName.ShouldBe(paramName);
    }

    [Fact(DisplayName = "Given an active fact, when Supersede is called, then the timestamp is recorded")]
    public void RecordSupersedeTimestamp()
    {
        var fact = MemoryFact.Create(
            MemoryScope.Project,
            "proj-1",
            MemoryFactKind.Standing,
            "ci",
            "github actions",
            MemorySource.Human,
            "admin",
            now);
        var supersededAt = now.AddMinutes(5);

        fact.Supersede(supersededAt);

        fact.SupersededAt.ShouldBe(supersededAt);
    }

    [Fact(DisplayName = "Given any subject or topic, when CanonicalKey is applied, then the shape is trimmed lowercase")]
    public void CanonicalizeArbitraryKeys()
    {
        MemoryFact.CanonicalKey("  MiXeD Case ").ShouldBe("mixed case");
        MemoryFact.CanonicalKey("already-clean").ShouldBe("already-clean");
    }
}
