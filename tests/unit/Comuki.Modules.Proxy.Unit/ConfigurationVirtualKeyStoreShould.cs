using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Resolving;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Builds the in-memory <see cref="VirtualKey"/> catalogue from <see cref="ProxyOptions"/>.</summary>
public sealed class ConfigurationVirtualKeyStoreShould
{
    [Fact(DisplayName = "Given a configured token, when FindAsync runs, then the matching VirtualKey is returned")]
    public async Task FindByTokenReturnsMatchAsync()
    {
        var projectId = Guid.NewGuid();
        Environment.SetEnvironmentVariable("STORE_FIND_UPSTREAM_KEY", "sk-test");

        var options = Options.Create(new ProxyOptions
        {
            VirtualKeys = [
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = "vkey_alpha",
                    ProjectId = projectId,
                    Provider = "openai",
                    BaseUrl = "https://api.openai.com",
                    ApiKeyEnvRef = "STORE_FIND_UPSTREAM_KEY",
                },
            ],
        });
        var store = new ConfigurationVirtualKeyStore(options, NullLogger<ConfigurationVirtualKeyStore>.Instance);

        var key = await store.FindAsync("vkey_alpha", TestContext.Current.CancellationToken);

        key.ShouldNotBeNull();
        key.ProjectId.Value.ShouldBe(projectId);
        key.Upstream.Provider.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given an unknown token, when FindAsync runs, then null is returned")]
    public async Task FindUnknownReturnsNullAsync()
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
                    ApiKeyEnvRef = "STORE_FIND_UPSTREAM_KEY",
                },
            ],
        });
        var store = new ConfigurationVirtualKeyStore(options, NullLogger<ConfigurationVirtualKeyStore>.Instance);

        var key = await store.FindAsync("vkey_omega", TestContext.Current.CancellationToken);

        key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given multiple configured keys, when ListAsync runs, then every key is returned")]
    public async Task ListAsyncReturnsAllKeysAsync()
    {
        var options = Options.Create(new ProxyOptions
        {
            VirtualKeys = [
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = "vkey_one",
                    ProjectId = Guid.NewGuid(),
                    Provider = "openai",
                    BaseUrl = "https://api.openai.com",
                    ApiKeyEnvRef = "x",
                },
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = "vkey_two",
                    ProjectId = Guid.NewGuid(),
                    Provider = "anthropic",
                    BaseUrl = "https://api.anthropic.com",
                    ApiKeyEnvRef = "y",
                },
            ],
        });
        var store = new ConfigurationVirtualKeyStore(options, NullLogger<ConfigurationVirtualKeyStore>.Instance);

        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        keys.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given an invalid row (empty token), when the store builds, then the row is dropped without breaking the snapshot")]
    public async Task InvalidRowsAreSkippedAsync()
    {
        var options = Options.Create(new ProxyOptions
        {
            VirtualKeys = [
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = string.Empty,
                    ProjectId = Guid.NewGuid(),
                    Provider = "openai",
                    BaseUrl = "https://api.openai.com",
                    ApiKeyEnvRef = "x",
                },
                new ProxyOptions.VirtualKeyConfiguration
                {
                    Token = "vkey_valid",
                    ProjectId = Guid.NewGuid(),
                    Provider = "openai",
                    BaseUrl = "https://api.openai.com",
                    ApiKeyEnvRef = "y",
                },
            ],
        });
        var store = new ConfigurationVirtualKeyStore(options, NullLogger<ConfigurationVirtualKeyStore>.Instance);

        var keys = await store.ListAsync(TestContext.Current.CancellationToken);

        keys.Count.ShouldBe(1);
        keys[0].Token.ShouldBe("vkey_valid");
    }
}
