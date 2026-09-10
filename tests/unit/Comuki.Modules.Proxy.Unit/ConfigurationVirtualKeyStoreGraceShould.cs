using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Resolving;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// Deletion grace period for <see cref="ConfigurationVirtualKeyStore"/>
/// (Q31). A removed key keeps resolving for
/// <see cref="ConfigurationVirtualKeyStore.DefaultGracePeriod"/>; after
/// the expiry, the key is gone again.
/// </summary>
public sealed class ConfigurationVirtualKeyStoreGraceShould
{
    private static readonly DateTimeOffset frozenNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a configured key, when RemoveAsync runs, then the next FindAsync still returns the key within the grace period")]
    public async Task FindAfterRemoveWithinGraceAsync()
    {
        var clock = new MutableTimeProvider(frozenNow);
        var store = NewStore(clock);

        await store.RemoveAsync("vkey_alpha", TestContext.Current.CancellationToken);

        var key = await store.FindAsync("vkey_alpha", TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
        key.Token.ShouldBe("vkey_alpha");
    }

    [Fact(DisplayName = "Given a removed key, when FindAsync runs after the grace period expires, then the key is null")]
    public async Task FindAfterGraceExpiresAsync()
    {
        var clock = new MutableTimeProvider(frozenNow);
        var store = NewStore(clock);

        await store.RemoveAsync("vkey_alpha", TestContext.Current.CancellationToken);
        clock.Advance(ConfigurationVirtualKeyStore.DefaultGracePeriod + TimeSpan.FromSeconds(1));

        var key = await store.FindAsync("vkey_alpha", TestContext.Current.CancellationToken);

        key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an active key that is never removed, when FindAsync runs, then ListAsync also includes it (no grace pollution)")]
    public async Task ActiveKeyNotGhostedAsync()
    {
        var clock = new MutableTimeProvider(frozenNow);
        var store = NewStore(clock);

        var key = await store.FindAsync("vkey_alpha", TestContext.Current.CancellationToken);
        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
        keys.Count.ShouldBe(1);
        keys[0].Token.ShouldBe("vkey_alpha");
    }

    private static ConfigurationVirtualKeyStore NewStore(MutableTimeProvider clock)
    {
        var options = Options.Create(new ProxyOptions
        {
            VirtualKeys = [
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = "vkey_alpha",
                    ProjectId = Guid.NewGuid(),
                    Provider = "openai",
                    BaseUrl = "https://api.openai.com",
                    ApiKeyEnvRef = "STORE_GRACE_UPSTREAM_KEY",
                },
            ],
        });

        return new ConfigurationVirtualKeyStore(
            clock,
            new VirtualKeySeed(options, new ConfigurableSecretResolver(), NullLogger<VirtualKeySeed>.Instance),
            NullLogger<ConfigurationVirtualKeyStore>.Instance);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset utcNow = initial;

        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void Advance(TimeSpan delta)
        {
            utcNow += delta;
        }
    }
}
