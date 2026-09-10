using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using VaultSharp;
using VaultSharp.V1.Commons;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// VaultSecretProvider tests — K/V v2 reads via VaultSharp with an
/// in-process TTL cache. The provider is exercised against a real
/// <see cref="MemoryCache"/> (the simplest concrete
/// <see cref="IMemoryCache"/>) and a fake <see cref="IVaultClient"/>
/// shaped by NSubstitute's auto-substitution of return types. Every
/// assertion runs in 1-2 ms; no real Vault traffic.
/// </summary>
public sealed class VaultSecretProviderShould
{
    private static readonly Secret<SecretData> emptySecret = new()
    {
        Data = new SecretData(),
    };

    /// <summary>Builds the provider under test.</summary>
    /// <param name="client">Substituted <see cref="IVaultClient"/> (caller seeds the read chain).</param>
    /// <param name="optionsValue">Bound <see cref="VaultSecretOptions"/>.</param>
    /// <param name="cache">Optional <see cref="IMemoryCache"/>; defaults to a real <see cref="MemoryCache"/> instance.</param>
    private static VaultSecretProvider BuildProvider(
        IVaultClient client,
        VaultSecretOptions optionsValue,
        IMemoryCache? cache = null)
    {
        return new VaultSecretProvider(
            Options.Create(optionsValue),
            client,
            NullLogger<VaultSecretProvider>.Instance,
            cache ?? new MemoryCache(new MemoryCacheOptions()));
    }

    private static VaultSecretOptions DefaultOptions(Action<VaultSecretOptions>? configure = null)
    {
        VaultSecretOptions options = new()
        {
            Enabled = true,
            Address = "https://vault.test.local:8200",
            TokenEnvRef = "COMUKI_VAULT_TOKEN",
            KvMount = "secret",
            CacheTtl = TimeSpan.FromSeconds(60),
        };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Creates a copy of <paramref name="source"/> with the given <paramref name="enabled"/> value (init-only properties).</summary>
    private static VaultSecretOptions WithEnabled(VaultSecretOptions source, bool enabled)
    {
        return new VaultSecretOptions
        {
            Enabled = enabled,
            Address = source.Address,
            TokenEnvRef = source.TokenEnvRef,
            KvMount = source.KvMount,
            CacheTtl = source.CacheTtl,
        };
    }

    [Fact(DisplayName = "Given a provider, its Scheme is the lowercase 'vault' string")]
    public void SchemeIsVault()
    {
        var provider = BuildProvider(Substitute.For<IVaultClient>(), DefaultOptions());

        provider.Scheme.ShouldBe("vault");
    }

    [Fact(DisplayName = "Given a vault ref, when ResolveAsync reads a KV v2 secret, then the field value is returned")]
    public async Task ResolveAsyncReturnsFieldValueAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var secret = new Secret<SecretData>
        {
            Data = new SecretData { Data = new Dictionary<string, object> { ["password"] = "s3cr3t" } },
        };
        client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: "prod/db", mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>())
            .Returns(secret);

        var provider = BuildProvider(client, DefaultOptions());

        var resolved = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);

        resolved.ShouldBe("s3cr3t");
    }

    [Fact(DisplayName = "Given a vault ref with a missing field, when ResolveAsync runs, then null is returned (the resolver turns it into SecretRefUnsetException)")]
    public async Task ResolveAsyncReturnsNullWhenFieldAbsentAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var secret = new Secret<SecretData>
        {
            Data = new SecretData { Data = new Dictionary<string, object> { ["other"] = "value" } },
        };
        client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>())
            .Returns(secret);

        var provider = BuildProvider(client, DefaultOptions());

        var resolved = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a vault ref, when ResolveAsync is called twice within the TTL, then the second call hits the cache and never reaches Vault")]
    public async Task ResolveAsyncCachesSecondCallAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var secret = new Secret<SecretData>
        {
            Data = new SecretData { Data = new Dictionary<string, object> { ["password"] = "s3cr3t" } },
        };
        client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>())
            .Returns(secret);

        var provider = BuildProvider(client, DefaultOptions());

        var first = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);
        var second = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);

        first.ShouldBe("s3cr3t");
        second.ShouldBe("s3cr3t");
        await client.V1.Secrets.KeyValue.V2
            .Received(1)
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given a provider with Enabled=false, when ResolveAsync runs, then null is returned and Vault is never contacted")]
    public async Task ResolveAsyncShortCircuitsWhenDisabledAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var provider = BuildProvider(client, WithEnabled(DefaultOptions(), enabled: false));

        var resolved = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
        await client.V1.Secrets.KeyValue.V2
            .DidNotReceive()
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given a vault ref with an empty field key, when ResolveAsync runs, then SecretRefFormatException is thrown before any Vault call")]
    public async Task ResolveAsyncThrowsWhenKeyMissingAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var provider = BuildProvider(client, DefaultOptions());

        await Should.ThrowAsync<SecretRefFormatException>(
            async () => await provider.ResolveAsync(new SecretRef("vault", "prod/db", null), TestContext.Current.CancellationToken));

        await client.V1.Secrets.KeyValue.V2
            .DidNotReceive()
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given a reference with a non-vault scheme, when the vault provider receives it, then SecretRefFormatException is thrown (defensive guard)")]
    public async Task ResolveAsyncThrowsWhenSchemeMismatchAsync()
    {
        var client = Substitute.For<IVaultClient>();
        var provider = BuildProvider(client, DefaultOptions());

        await Should.ThrowAsync<SecretRefFormatException>(
            async () => await provider.ResolveAsync(new SecretRef("file", "/etc/foo", null), TestContext.Current.CancellationToken));

        await client.V1.Secrets.KeyValue.V2
            .DidNotReceive()
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given a VaultSharp response with a null inner data dict, when ResolveAsync runs, then null is returned (no NRE)")]
    public async Task ResolveAsyncHandlesNullInnerDataAsync()
    {
        var client = Substitute.For<IVaultClient>();
        client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>())
            .Returns(emptySecret);

        var provider = BuildProvider(client, DefaultOptions());

        var resolved = await provider.ResolveAsync(new SecretRef("vault", "prod/db", "password"), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }
}
