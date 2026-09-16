using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The consolidation sleep-cycle rules in isolation: the promote/decay
/// candidate expressions (compiled and invoked — the exact expressions
/// the store passes to SQL, so tested logic and executed criteria cannot
/// drift), the decay backdate policy, and the entity's read registration.
/// </summary>
public sealed class MemoryConsolidationRulesShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan promoteMinAge = TimeSpan.FromHours(1);

    [Fact(DisplayName = "Given an ephemeral fact read 3 times and older than the min age, when promote candidates are evaluated, then it qualifies")]
    public void PromoteCandidateQualifiesAtThreshold()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, createdAt: now.AddHours(-2), readCount: 3);

        IsPromoteCandidate(fact).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an ephemeral fact below the read threshold, when promote candidates are evaluated, then it does not qualify")]
    public void PromoteCandidateRejectsTooFewReads()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, createdAt: now.AddHours(-2), readCount: 2);

        IsPromoteCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an ephemeral fact younger than the min age, when promote candidates are evaluated, then it does not qualify even at 3 reads")]
    public void PromoteCandidateRejectsFreshFacts()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, createdAt: now.AddMinutes(-30), readCount: 3);

        IsPromoteCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a standing fact, when promote candidates are evaluated, then it does not qualify regardless of reads")]
    public void PromoteCandidateRejectsStandingFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddHours(-2), readCount: 10);

        IsPromoteCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a superseded ephemeral fact, when promote candidates are evaluated, then it does not qualify")]
    public void PromoteCandidateRejectsSupersededFacts()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, createdAt: now.AddHours(-2), readCount: 3);
        fact.Supersede(now);

        IsPromoteCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a standing fact last read 91 days ago, when decay candidates are evaluated, then it qualifies")]
    public void DecayCandidateQualifiesAfterUnreadWindow()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddDays(-200), lastReadAt: now.AddDays(-91));

        IsDecayCandidate(fact).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a never-read standing fact created 91 days ago, when decay candidates are evaluated, then it qualifies")]
    public void DecayCandidateCountsFromCreationWhenNeverRead()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddDays(-91), lastReadAt: null);

        IsDecayCandidate(fact).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a standing fact read 10 days ago, when decay candidates are evaluated, then it does not qualify")]
    public void DecayCandidateRejectsRecentlyReadFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddDays(-200), lastReadAt: now.AddDays(-10));

        IsDecayCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a never-read standing fact created 10 days ago, when decay candidates are evaluated, then it does not qualify")]
    public void DecayCandidateRejectsFreshNeverReadFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddDays(-10), lastReadAt: null);

        IsDecayCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an ephemeral fact unread for months, when decay candidates are evaluated, then it does not qualify — decay is a standing-fate only")]
    public void DecayCandidateRejectsEphemeralFacts()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, createdAt: now.AddDays(-200), lastReadAt: now.AddDays(-200));

        IsDecayCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a superseded standing fact, when decay candidates are evaluated, then it does not qualify")]
    public void DecayCandidateRejectsSupersededFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now.AddDays(-200), lastReadAt: now.AddDays(-200));
        fact.Supersede(now);

        IsDecayCandidate(fact).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a decay instant, when DecayCreatedAt is applied, then exactly the grace window remains under the sweep horizon")]
    public void DecayBackdateLeavesGraceWindow()
    {
        var decayedAt = MemoryFactPolicy.DecayCreatedAt(now);

        decayedAt.ShouldBe(now - (MemoryFactPolicy.EphemeralTtl - MemoryFactPolicy.DecayRemainingTtl));
        // the sweep cutoff measured at the decay instant must sit one
        // grace window BEFORE the backdated creation
        (decayedAt - (now - MemoryFactPolicy.EphemeralTtl)).ShouldBe(MemoryFactPolicy.DecayRemainingTtl);
    }

    [Fact(DisplayName = "Given an unread fact, when RegisterRead is called twice, then the count reaches two and the last read stamps the latest instant")]
    public void RegisterReadCountsAndStamps()
    {
        var fact = Fact(MemoryFactKind.Standing, createdAt: now);

        fact.RegisterRead(now.AddMinutes(1));
        fact.RegisterRead(now.AddMinutes(2));

        fact.ReadCount.ShouldBe(2);
        fact.LastReadAt.ShouldBe(now.AddMinutes(2));
    }

    private static bool IsPromoteCandidate(MemoryFact fact)
    {
        return MemoryFactConsolidation.PromoteCandidates(now, 3, promoteMinAge).Compile()(fact);
    }

    private static bool IsDecayCandidate(MemoryFact fact)
    {
        return MemoryFactConsolidation.DecayCandidates(now, TimeSpan.FromDays(90)).Compile()(fact);
    }

    private static MemoryFact Fact(MemoryFactKind kind, DateTimeOffset createdAt, int readCount = 0, DateTimeOffset? lastReadAt = null)
    {
        var fact = MemoryFact.Create(
            MemoryScope.User,
            "user-1",
            kind,
            "deploy",
            "prefers docker compose",
            MemorySource.Chat,
            "user-1",
            createdAt);
        // one read is the minimum to carry a non-null lastReadAt at all
        var reads = Math.Max(readCount, lastReadAt is null ? 0 : 1);
        for (var read = 0; read < reads; read++)
        {
            fact.RegisterRead(lastReadAt ?? createdAt);
        }

        return fact;
    }
}
