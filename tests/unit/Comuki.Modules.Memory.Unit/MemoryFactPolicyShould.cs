using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// The 14-day ephemeral TTL and the visibility rule shared by search,
/// digest and the sweep worker (add-chat-memory contract).
/// </summary>
public sealed class MemoryFactPolicyShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the policy, then the ephemeral TTL is exactly 14 days")]
    public void PinEphemeralTtlToFourteenDays()
    {
        MemoryFactPolicy.EphemeralTtl.ShouldBe(TimeSpan.FromDays(14));
        // same provider/dimension as the knowledge schema (one embedding
        // model serves both stores)
        MemoryFactPolicy.EmbeddingDimensions.ShouldBe(1536);
    }

    [Theory(DisplayName = "Given a custom ephemeral TTL, when the creation instant is computed, then the fact expires exactly when the TTL elapses")]
    [InlineData(2, 0, 0)]    // 2h ttl
    [InlineData(24, 0, 0)]   // 1d ttl
    [InlineData(336, 0, 0)]  // 14d ttl — exactly the full horizon
    [InlineData(720, 0, 0)]  // above the horizon — clamped to the full horizon
    public void BackdateEphemeralCreationSoTheFixedHorizonExpiresOnSchedule(int ttlHours, int ttlMinutes, int ttlSeconds)
    {
        var ttl = new TimeSpan(ttlHours, ttlMinutes, ttlSeconds);

        var createdAt = MemoryFactPolicy.EphemeralCreatedAt(now, ttl);

        createdAt.ShouldBeLessThanOrEqualTo(now);
        var effectiveTtl = ttl <= MemoryFactPolicy.EphemeralTtl ? ttl : MemoryFactPolicy.EphemeralTtl;
        (createdAt + MemoryFactPolicy.EphemeralTtl - now).ShouldBe(effectiveTtl);
        // and the visibility rule agrees with the sweep horizon
        MemoryFactPolicy.IsExpired(Fact(MemoryFactKind.Ephemeral, createdAt), now + effectiveTtl).ShouldBeTrue();
        MemoryFactPolicy.IsExpired(Fact(MemoryFactKind.Ephemeral, createdAt), now + effectiveTtl - TimeSpan.FromMinutes(1)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a non-positive TTL, when the creation instant is computed, then ArgumentOutOfRangeException refuses it")]
    public void RefuseNonPositiveCustomTtl()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => MemoryFactPolicy.EphemeralCreatedAt(now, TimeSpan.Zero));
        Should.Throw<ArgumentOutOfRangeException>(static () => MemoryFactPolicy.EphemeralCreatedAt(now, TimeSpan.FromMinutes(-5)));
    }

    [Theory(DisplayName = "Given an ephemeral fact, when the TTL boundary is crossed, then IsExpired flips exactly at 14 days")]
    [InlineData(13, 23, 59, false)]
    [InlineData(14, 0, 0, true)]
    [InlineData(20, 0, 0, true)]
    public void ExpireEphemeralAtFourteenDays(int days, int hours, int minutes, bool expected)
    {
        var fact = Fact(MemoryFactKind.Ephemeral, now);

        var expired = MemoryFactPolicy.IsExpired(fact, now.AddDays(days).AddHours(hours).AddMinutes(minutes));

        expired.ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a standing fact, when time passes, then it never expires")]
    public void NeverExpireStandingFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, now);

        MemoryFactPolicy.IsExpired(fact, now.AddYears(5)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a superseded fact, when visibility is checked, then it is invisible regardless of age")]
    public void HideSupersededFacts()
    {
        var fact = Fact(MemoryFactKind.Standing, now);
        fact.Supersede(now.AddMinutes(1));

        MemoryFactPolicy.IsVisible(fact, now.AddMinutes(2)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an expired ephemeral fact, when visibility is checked, then it is invisible")]
    public void HideExpiredEphemeralFacts()
    {
        var fact = Fact(MemoryFactKind.Ephemeral, now);

        MemoryFactPolicy.IsVisible(fact, now.AddDays(15)).ShouldBeFalse();
        MemoryFactPolicy.IsVisible(fact, now.AddDays(1)).ShouldBeTrue();
    }

    private static MemoryFact Fact(MemoryFactKind kind, DateTimeOffset createdAt)
    {
        return MemoryFact.Create(
            MemoryScope.User,
            "user-1",
            kind,
            "topic",
            "text",
            MemorySource.Chat,
            "user-1",
            createdAt);
    }
}
