using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Resolving;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Runtime mint overlay of <see cref="ConfigurationVirtualKeyStore"/> (issue #122).</summary>
public sealed class ConfigurationVirtualKeyStoreMintShould
{
    [Fact(DisplayName = "Given a minted token, when FindAsync runs, then it resolves with the claim's project, expiry and inherited upstream")]
    public async Task MintedTokenResolvesAsync()
    {
        var projectId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
        var store = NewStore(KeyRow("vkey_alpha", projectId, "anthropic", "https://api.anthropic.com"));

        await store.MintAsync("minted_one", new ProjectId(projectId), workItemId, leaseUntil, cancellationToken: TestContext.Current.CancellationToken);

        var key = await store.FindAsync("minted_one", cancellationToken: TestContext.Current.CancellationToken);
        key.ShouldNotBeNull();
        key.ProjectId.Value.ShouldBe(projectId);
        key.WorkItemId.ShouldBe(workItemId);
        key.ExpiresAt.ShouldBe(leaseUntil);
        key.BudgetUsd.ShouldBeNull();
        key.Upstream.BaseUrl.ShouldBe("https://api.anthropic.com");
    }

    [Fact(DisplayName = "Given a project without its own key and both providers configured, when minting, then the anthropic upstream is inherited")]
    public async Task MintInheritsAnthropicUpstreamAsync()
    {
        var projectId = Guid.NewGuid();
        var store = NewStore(
            KeyRow("vkey_openai", Guid.NewGuid(), "openai", "https://api.openai.com"),
            KeyRow("vkey_anthropic", Guid.NewGuid(), "anthropic", "https://api.anthropic.com"));

        await store.MintAsync("minted_two", new ProjectId(projectId), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken: TestContext.Current.CancellationToken);

        var key = await store.FindAsync("minted_two", cancellationToken: TestContext.Current.CancellationToken);
        key.ShouldNotBeNull();
        key.Upstream.Provider.ShouldBe("anthropic");
    }

    [Fact(DisplayName = "Given a minted key, when RemoveAsync runs, then FindAsync is null immediately (no deletion grace for mints)")]
    public async Task RemoveMintedKeyKillsItAtOnceAsync()
    {
        var store = NewStore(KeyRow("vkey_alpha", Guid.NewGuid(), "openai", "https://api.openai.com"));
        await store.MintAsync("minted_three", new ProjectId(Guid.NewGuid()), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken: TestContext.Current.CancellationToken);

        await store.RemoveAsync("minted_three", cancellationToken: TestContext.Current.CancellationToken);

        var key = await store.FindAsync("minted_three", cancellationToken: TestContext.Current.CancellationToken);
        key.ShouldBeNull("a revoked mint must fail authentication immediately — the 60s config-key grace must not resurrect it");
    }

    [Fact(DisplayName = "Given a minted and a config-seeded key, when the mint is removed, then the config key still resolves")]
    public async Task ConfigKeysSurviveMintLifecycleAsync()
    {
        var projectId = Guid.NewGuid();
        var store = NewStore(KeyRow("vkey_alpha", projectId, "openai", "https://api.openai.com"));
        await store.MintAsync("minted_four", new ProjectId(projectId), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken: TestContext.Current.CancellationToken);

        await store.RemoveAsync("minted_four", cancellationToken: TestContext.Current.CancellationToken);

        var configKey = await store.FindAsync("vkey_alpha", cancellationToken: TestContext.Current.CancellationToken);
        configKey.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a minted key beside a config-seeded key, when ListAsync runs, then both are listed")]
    public async Task ListIncludesMintedKeysAsync()
    {
        var projectId = Guid.NewGuid();
        var store = NewStore(KeyRow("vkey_alpha", projectId, "openai", "https://api.openai.com"));
        await store.MintAsync("minted_five", new ProjectId(projectId), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken: TestContext.Current.CancellationToken);

        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        keys.Count.ShouldBe(2);
        keys.ShouldContain(static key => key.Token == "vkey_alpha");
        keys.ShouldContain(static key => key.Token == "minted_five");
    }

    [Fact(DisplayName = "Given no configured provider keys, when minting, then the mint fails loud instead of producing a key with no upstream")]
    public async Task MintWithoutAnyUpstreamThrowsAsync()
    {
        var store = NewStore();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await store.MintAsync("minted_six", new ProjectId(Guid.NewGuid()), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given allowed models on a mint, when FindAsync runs, then the allow-list is carried on the key")]
    public async Task MintCarriesAllowedModelsAsync()
    {
        var projectId = Guid.NewGuid();
        var store = NewStore(KeyRow("vkey_alpha", projectId, "openai", "https://api.openai.com"));

        await store.MintAsync(
            "minted_seven", new ProjectId(projectId), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2),
            ["gpt-4o-mini"], cancellationToken: TestContext.Current.CancellationToken);

        var key = await store.FindAsync("minted_seven", cancellationToken: TestContext.Current.CancellationToken);
        key.ShouldNotBeNull();
        key.AllowedModels.ShouldBe(["gpt-4o-mini"]);
    }

    private static ConfigurationVirtualKeyStore NewStore(params ProxyOptions.VirtualKeyConfiguration[] keys)
    {
        var options = new ProxyOptions { VirtualKeys = [.. keys] };
        return new ConfigurationVirtualKeyStore(
            TimeProvider.System,
            new VirtualKeySeed(Options.Create(options), new ConfigurableSecretResolver(), NullLogger<VirtualKeySeed>.Instance),
            NullLogger<ConfigurationVirtualKeyStore>.Instance);
    }

    private static ProxyOptions.VirtualKeyConfiguration KeyRow(
        string token,
        Guid projectId,
        string provider,
        string baseUrl)
    {
        return new ProxyOptions.VirtualKeyConfiguration
        {
            Token = token,
            ProjectId = projectId,
            Provider = provider,
            BaseUrl = baseUrl,
            ApiKeyEnvRef = "MINT_TEST_UPSTREAM_KEY",
        };
    }
}
