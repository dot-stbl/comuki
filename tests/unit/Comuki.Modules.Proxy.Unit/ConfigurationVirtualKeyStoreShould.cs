using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Resolving;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Builds the in-memory <see cref="Application.Models.VirtualKey"/> catalogue from <see cref="ProxyOptions"/>.</summary>
public sealed class ConfigurationVirtualKeyStoreShould
{
    [Fact(DisplayName = "Given a configured token, when FindAsync runs, then the matching VirtualKey is returned")]
    public async Task FindByTokenReturnsMatchAsync()
    {
        var projectId = Guid.NewGuid();
        var resolver = new ConfigurableSecretResolver();
        resolver.Map["STORE_FIND_UPSTREAM_KEY"] = "sk-test";

        var store = NewStore(WithKeys(KeyRow("vkey_alpha", projectId, "STORE_FIND_UPSTREAM_KEY")), resolver);

        var key = await store.FindAsync("vkey_alpha", TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
        key.ProjectId.Value.ShouldBe(projectId);
        key.Upstream.Provider.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given an unknown token, when FindAsync runs, then null is returned")]
    public async Task FindUnknownReturnsNullAsync()
    {
        var store = NewStore(WithKeys(KeyRow("vkey_alpha", Guid.NewGuid(), "STORE_FIND_UPSTREAM_KEY")));

        var key = await store.FindAsync("vkey_omega", TestContext.Current.CancellationToken);

        key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given multiple configured keys, when ListAsync runs, then every key is returned")]
    public async Task ListAsyncReturnsAllKeysAsync()
    {
        var store = NewStore(WithKeys(
            KeyRow("vkey_one", Guid.NewGuid(), "x"),
            KeyRow("vkey_two", Guid.NewGuid(), "y", provider: "anthropic", baseUrl: "https://api.anthropic.com")));

        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        keys.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given an invalid row (empty token), when the store builds, then the row is dropped without breaking the snapshot")]
    public async Task InvalidRowsAreSkippedAsync()
    {
        var store = NewStore(WithKeys(
            KeyRow(string.Empty, Guid.NewGuid(), "x"),
            KeyRow("vkey_valid", Guid.NewGuid(), "y")));

        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        keys.Count.ShouldBe(1);
        keys[0].Token.ShouldBe("vkey_valid");
    }

    [Fact(DisplayName = "Given a virtual key whose ApiKeyEnvRef resolves through the secret resolver, when the store builds, then the resolved value is available without a direct env read")]
    public async Task ResolverBackedSeedAsync()
    {
        var projectId = Guid.NewGuid();
        var resolver = new ConfigurableSecretResolver();
        resolver.Map["RESOLVER_BACKED_KEY"] = "sk-resolver";

        var store = NewStore(WithKeys(KeyRow("vkey_resolver", projectId, "RESOLVER_BACKED_KEY")), resolver);

        var key = await store.FindAsync("vkey_resolver", TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
        key.Upstream.ApiKeyEnvRef.ShouldBe("RESOLVER_BACKED_KEY");
        resolver.Received.ShouldContain("RESOLVER_BACKED_KEY");
    }

    [Fact(DisplayName = "Given a virtual key whose ApiKeyEnvRef resolves empty, when the store builds, then the row still lands (a runtime 502 surfaces the missing value)")]
    public async Task EmptyResolvedRefStillSeedsAsync()
    {
        var resolver = new ConfigurableSecretResolver();

        var store = NewStore(WithKeys(KeyRow("vkey_empty", Guid.NewGuid(), "UNSET_REF")), resolver);

        var key = await store.FindAsync("vkey_empty", TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
    }

    private static ProxyOptions WithKeys(params ProxyOptions.VirtualKeyConfiguration[] keys)
    {
        return new ProxyOptions { VirtualKeys = [.. keys] };
    }

    private static ProxyOptions.VirtualKeyConfiguration KeyRow(
        string token,
        Guid projectId,
        string apiKeyEnvRef,
        string provider = "openai",
        string baseUrl = "https://api.openai.com")
    {
        return new ProxyOptions.VirtualKeyConfiguration
        {
            Token = token,
            ProjectId = projectId,
            Provider = provider,
            BaseUrl = baseUrl,
            ApiKeyEnvRef = apiKeyEnvRef,
        };
    }

    private static ConfigurationVirtualKeyStore NewStore(ProxyOptions options, ConfigurableSecretResolver? resolver = null)
    {
        return new ConfigurationVirtualKeyStore(
            TimeProvider.System,
            new VirtualKeySeed(Options.Create(options), resolver ?? new ConfigurableSecretResolver(), NullLogger<VirtualKeySeed>.Instance),
            NullLogger<ConfigurationVirtualKeyStore>.Instance);
    }
}
