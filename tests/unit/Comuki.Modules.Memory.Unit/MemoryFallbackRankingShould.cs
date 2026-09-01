using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The embedding-free ranking — the contract's hard floor: standing facts
/// before ephemeral ones, then freshest first, capped by the limit.
/// </summary>
public sealed class MemoryFallbackRankingShould
{
    private static readonly DateTimeOffset baseTime = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given mixed kinds, when ranked, then standing facts come before ephemeral ones")]
    public void RankStandingBeforeEphemeral()
    {
        var freshEphemeral = Fact("fresh-ephemeral", MemoryFactKind.Ephemeral, baseTime.AddDays(1));
        var oldStanding = Fact("old-standing", MemoryFactKind.Standing, baseTime.AddDays(-10));

        var ranked = MemoryFallbackRanking.Rank([freshEphemeral, oldStanding], 10);

        ranked.First().TopicKey.ShouldBe("old-standing");
    }

    [Fact(DisplayName = "Given facts of one kind, when ranked, then the freshest comes first")]
    public void RankFreshestFirstWithinKind()
    {
        var older = Fact("older", MemoryFactKind.Standing, baseTime.AddDays(-3));
        var newer = Fact("newer", MemoryFactKind.Standing, baseTime);

        var ranked = MemoryFallbackRanking.Rank([older, newer], 10);

        ranked.Select(static fact => fact.TopicKey).ShouldBe(["newer", "older"]);
    }

    [Fact(DisplayName = "Given more facts than the limit, when ranked, then only the limit survives")]
    public void ApplyLimit()
    {
        var facts = Enumerable.Range(0, 10)
            .Select(static offset => Fact($"topic-{offset}", MemoryFactKind.Standing, baseTime.AddDays(-offset)))
            .ToArray();

        var ranked = MemoryFallbackRanking.Rank(facts, 3);

        ranked.Count.ShouldBe(3);
        ranked.Select(static fact => fact.TopicKey).ShouldBe(["topic-0", "topic-1", "topic-2"]);
    }

    [Fact(DisplayName = "Given no facts, when ranked, then the result is empty")]
    public void RankNothing()
    {
        MemoryFallbackRanking.Rank([], 5).ShouldBeEmpty();
    }

    private static MemoryFactView Fact(string topicKey, MemoryFactKind kind, DateTimeOffset createdAt)
    {
        return new MemoryFactView(
            MemoryFactId.New(),
            MemoryScope.User,
            "user-1",
            kind,
            topicKey,
            $"text about {topicKey}",
            MemorySource.Chat,
            "user-1",
            createdAt);
    }
}
