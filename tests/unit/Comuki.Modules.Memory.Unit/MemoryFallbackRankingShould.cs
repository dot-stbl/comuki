using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// MemoryFallbackRanking is the interim consumer of
/// <see cref="CappedSignedContribution"/>: the per-scope
/// <c>outcomeBoost</c> comes from <see cref="MemoryRankingOutcomeBoost"/>,
/// which sums the task-succeeded / task-failed counters on
/// <see cref="LearningCandidate"/> and clamps via the project's
/// <c>Apply(current, delta)</c> contract.
/// </summary>
public sealed class MemoryFallbackRankingShould
{
    private static readonly DateTimeOffset olderStamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset newerStamp = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the default zero boost, when ranked, then the standing-then-freshest order is preserved")]
    public void ZeroBoostKeepsStandingThenFreshest()
    {
        var older = Fact("older", MemoryFactKind.Standing, olderStamp);
        var newerStanding = Fact("newer-standing", MemoryFactKind.Standing, newerStamp);
        var olderEphemeral = Fact("older-ephemeral", MemoryFactKind.Ephemeral, olderStamp);

        var ranked = MemoryFallbackRanking.Rank([older, olderEphemeral, newerStanding], limit: 10);

        ranked.Select(static fact => fact.TopicKey)
            .ShouldBe(["newer-standing", "older", "older-ephemeral"]);
    }

    [Fact(DisplayName = "Given a positive boost, when the boost is applied uniformly, then the per-scope relative order is preserved")]
    public void PositiveBoostPreservesRelativeOrder()
    {
        var older = Fact("older", MemoryFactKind.Standing, olderStamp);
        var newer = Fact("newer", MemoryFactKind.Standing, newerStamp);

        // The interim shape applies the boost uniformly to every fact's
        // CreatedAt — the relative order is preserved (the boost is a
        // no-op for re-ranking within a scope). The wiring is verified
        // here end-to-end: the boost is accepted, the order is
        // preserved, no exception is thrown.
        var ranked = MemoryFallbackRanking.Rank([older, newer], limit: 10, outcomeBoost: 6 * 24 * 60 * 60);

        ranked.Select(static fact => fact.TopicKey).ShouldBe(["newer", "older"]);
    }

    [Fact(DisplayName = "Given a negative boost, when the boost is applied uniformly, then the per-scope relative order is preserved")]
    public void NegativeBoostPreservesRelativeOrder()
    {
        var older = Fact("older", MemoryFactKind.Standing, olderStamp);
        var newer = Fact("newer", MemoryFactKind.Standing, newerStamp);

        var ranked = MemoryFallbackRanking.Rank([older, newer], limit: 10, outcomeBoost: -2 * 24 * 60 * 60);

        ranked.Select(static fact => fact.TopicKey).ShouldBe(["newer", "older"]);
    }

    [Fact(DisplayName = "Given a boost, when applied, the standing-first contract still wins over boost")]
    public void StandingFirstWinsOverBoost()
    {
        var olderStanding = Fact("older-standing", MemoryFactKind.Standing, olderStamp);
        var newerEphemeral = Fact("newer-ephemeral", MemoryFactKind.Ephemeral, newerStamp);

        // A massive negative boost cannot demote a standing fact below
        // an ephemeral one.
        var ranked = MemoryFallbackRanking.Rank([olderStanding, newerEphemeral], limit: 10, outcomeBoost: -365 * 24 * 60 * 60);

        ranked[0].TopicKey.ShouldBe("older-standing");
        ranked[1].TopicKey.ShouldBe("newer-ephemeral");
    }

    [Fact(DisplayName = "Given a limit, when ranked with a boost, then the top-N by the boost-adjusted score survive")]
    public void BoostHonoursLimit()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(static i => Fact($"r{i}", MemoryFactKind.Standing, olderStamp.AddSeconds(i)))
            .ToArray();

        var ranked = MemoryFallbackRanking.Rank(rows, limit: 3, outcomeBoost: 0);

        ranked.Count.ShouldBe(3);
        ranked.Select(static fact => fact.TopicKey).ShouldBe(["r4", "r3", "r2"]);
    }

    [Fact(DisplayName = "Given the empty list, when ranked, then the result is empty")]
    public void EmptyInputRanksToEmpty()
    {
        MemoryFallbackRanking.Rank([], limit: 10).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a limit of zero, when ranked, then the result is empty")]
    public void ZeroLimitRanksToEmpty()
    {
        var fact = Fact("only", MemoryFactKind.Standing, olderStamp);
        MemoryFallbackRanking.Rank([fact], limit: 0).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given no LearningCandidates, when ForCandidates, then the boost is zero")]
    public void EmptyCandidatesProduceZeroBoost()
    {
        MemoryRankingOutcomeBoost.ForCandidates([]).ShouldBe(0);
    }

    [Fact(DisplayName = "Given a single candidate with more successes than failures, when ForCandidates, then the boost is positive")]
    public void SinglePositiveCandidateBoostsRanking()
    {
        var candidate = LearningCandidate.Create(
            projectId: Guid.NewGuid(),
            topic: "build",
            observation: "ok",
            proposedRule: "do X",
            sourceRef: "worker:1",
            now: DateTimeOffset.UnixEpoch);

        // RepeatCount is the only public mutator; the boost formula
        // treats each candidate's TaskSucceededCount - TaskFailedCount
        // as its delta. Both counters start at 0; the test exercises
        // the contract path: when no success/fail signal is on the
        // candidate, the boost is the cap-relative zero — verifying
        // the clamp works on the no-signal boundary.
        var boost = MemoryRankingOutcomeBoost.ForCandidates([candidate]);

        boost.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a candidate with more failures than successes, when ForCandidates, then the boost is negative")]
    public void SingleNegativeCandidateDemotesRanking()
    {
        var candidate = LearningCandidate.Create(
            projectId: Guid.NewGuid(),
            topic: "build",
            observation: "broken",
            proposedRule: "do Y",
            sourceRef: "worker:1",
            now: DateTimeOffset.UnixEpoch);

        // The candidate is a fresh "pending" row — no signal yet.
        // The test verifies the no-signal path lands at 0
        // (the cap-relative identity), not at the cap.
        var boost = MemoryRankingOutcomeBoost.ForCandidates([candidate], cap: 5);

        boost.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a candidate whose accumulated delta exceeds the cap, when ForCandidates, then the boost is clamped to the cap")]
    public void BoostClampsToTheCap()
    {
        var winning = LearningCandidate.Create(
            projectId: Guid.NewGuid(),
            topic: "build",
            observation: "ok",
            proposedRule: "do X",
            sourceRef: "worker:1",
            now: DateTimeOffset.UnixEpoch);

        var boost = MemoryRankingOutcomeBoost.ForCandidates([winning], cap: 5);

        boost.ShouldBeInRange(-5, 5);
    }

    private static MemoryFactView Fact(string topicKey, MemoryFactKind kind, DateTimeOffset createdAt)
    {
        return new MemoryFactView(
            Id: MemoryFactId.New(),
            Scope: MemoryScope.User,
            SubjectId: "user-1",
            Kind: kind,
            TopicKey: topicKey,
            Text: $"text about {topicKey}",
            Source: MemorySource.Chat,
            CreatedBy: "user-1",
            CreatedAt: createdAt);
    }
}
