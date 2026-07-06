using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Comuki.Platform.Routing.Services;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Routing.Unit.KeyRotation;

public sealed class KeyPoolShould
{
    private static readonly DateTimeOffset start = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

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
        var time = new MutableTimeProvider(start);
        return (new KeyPool(options, time), time);
    }

    [Fact(DisplayName = "Given a pool with multiple live keys, when acquiring, then returns the first key")]
    public void ReturnFirstKeyWhenNoneExhausted()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.TryAcquire().ShouldBe("a");
    }

    [Fact(DisplayName = "Given the first key is in cooldown, when acquiring, then returns the next live key")]
    public void SkipExhaustedKey()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.MarkExhausted("a", retryAfter: null);

        pool.TryAcquire().ShouldBe("b");
    }

    [Fact(DisplayName = "Given every key is in cooldown, when acquiring, then returns null")]
    public void ReturnNullWhenAllKeysExhausted()
    {
        var (pool, _) = CreateSut(["a", "b"]);

        pool.MarkExhausted("a", retryAfter: null);
        pool.MarkExhausted("b", retryAfter: null);

        pool.TryAcquire().ShouldBeNull();
    }

    [Fact(DisplayName = "Given a key in cooldown, when the cooldown elapses, then the key becomes acquirable again")]
    public void RecoverKeyAfterCooldownElapses()
    {
        var (pool, time) = CreateSut(["a"], defaultCooldown: TimeSpan.FromMinutes(30));

        pool.MarkExhausted("a", retryAfter: null);
        pool.TryAcquire().ShouldBeNull();

        time.Advance(TimeSpan.FromMinutes(31));

        pool.TryAcquire().ShouldBe("a");
    }

    [Fact(DisplayName = "Given a Retry-After value, when marking a key exhausted, then cooldown lasts exactly Retry-After")]
    public void UseRetryAfterWhenProvided()
    {
        var (pool, time) = CreateSut(["a"], defaultCooldown: TimeSpan.FromHours(1));

        pool.MarkExhausted("a", retryAfter: TimeSpan.FromMinutes(5));

        time.Advance(TimeSpan.FromMinutes(4));
        pool.TryAcquire().ShouldBeNull();

        time.Advance(TimeSpan.FromMinutes(2));
        pool.TryAcquire().ShouldBe("a");
    }

    [Fact(DisplayName = "Given a pool of N keys, when reading Count, then returns N")]
    public void ReturnPoolSize()
    {
        var (pool, _) = CreateSut(["a", "b", "c"]);

        pool.Count.ShouldBe(3);
    }
}
