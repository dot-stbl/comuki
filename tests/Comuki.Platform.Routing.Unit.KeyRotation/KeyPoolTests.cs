using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Comuki.Platform.Routing.Services;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Routing.Unit.KeyRotation;

public sealed class KeyPoolTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private static (KeyPool Pool, MutableTimeProvider Time) CreateSut(
        string[] keys,
        TimeSpan? defaultCooldown = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RotationOptions
        {
            ApiKeys = keys,
            UpstreamUrl = "https://upstream.example",
            DefaultCooldown = defaultCooldown ?? TimeSpan.FromHours(1),
            ExhaustionRules = [new ExhaustionRule { StatusCode = 429 }],
        });
        var time = new MutableTimeProvider(Start);
        return (new KeyPool(options, time), time);
    }

    [Fact]
    public void TryAcquire_ReturnsFirstKey_WhenNoneExhausted()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.TryAcquire().ShouldBe("a");
    }

    [Fact]
    public void TryAcquire_SkipsExhaustedKey()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.MarkExhausted("a", retryAfter: null);

        pool.TryAcquire().ShouldBe("b");
    }

    [Fact]
    public void TryAcquire_ReturnsNull_WhenAllExhausted()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.MarkExhausted("a", retryAfter: null);
        pool.MarkExhausted("b", retryAfter: null);

        pool.TryAcquire().ShouldBeNull();
    }

    [Fact]
    public void TryAcquire_RecoversKey_AfterCooldownElapses()
    {
        var (pool, time) = CreateSut(["a"], defaultCooldown: TimeSpan.FromMinutes(30));

        pool.MarkExhausted("a", retryAfter: null);
        pool.TryAcquire().ShouldBeNull();

        time.Advance(TimeSpan.FromMinutes(31));

        pool.TryAcquire().ShouldBe("a");
    }

    [Fact]
    public void MarkExhausted_UsesRetryAfter_WhenProvided()
    {
        var (pool, time) = CreateSut(["a"], defaultCooldown: TimeSpan.FromHours(1));

        pool.MarkExhausted("a", retryAfter: TimeSpan.FromMinutes(5));

        time.Advance(TimeSpan.FromMinutes(4));
        pool.TryAcquire().ShouldBeNull();

        time.Advance(TimeSpan.FromMinutes(2));
        pool.TryAcquire().ShouldBe("a");
    }

    [Fact]
    public void Count_ReturnsPoolSize()
    {
        var (pool, _) = CreateSut(["a", "b", "c"]);

        pool.Count.ShouldBe(3);
    }
}
