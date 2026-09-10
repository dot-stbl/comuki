using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using VaultSharp;
using Xunit;

namespace Comuki.Host.Integration.Secrets;

/// <summary>
/// Unit-style coverage for the internal
/// <see cref="VaultSecretClientFactory"/> (issue #52 slice-2 audit H2 —
/// the factory previously had zero tests; the provider fixture built
/// its own <see cref="VaultClient"/> directly). Reached through the
/// <c>InternalsVisibleTo</c> grant in <c>Comuki.Host.csproj</c>, like
/// the other host integration projects. No Docker needed: VaultSharp
/// constructs the client lazily — <c>Build</c> performs no Vault
/// round-trip, so these run on every CI host.
/// </summary>
public sealed class VaultSecretClientFactoryShould : IDisposable
{
    private const string CustomTokenEnvRef = "COMUKI_TEST_FACTORY_VAULT_TOKEN";

    public VaultSecretClientFactoryShould()
    {
        Environment.SetEnvironmentVariable(CustomTokenEnvRef, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CustomTokenEnvRef, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
    }

    [Fact(DisplayName = "Given Enabled=true and the configured token env var set, when Build runs, then a real VaultClient is constructed")]
    public void BuildReturnsVaultClientWhenEnabledAndTokenSet()
    {
        Environment.SetEnvironmentVariable(CustomTokenEnvRef, "factory-unit-bootstrap-token");
        var provider = BuildServiceProvider(new VaultSecretOptions
        {
            Enabled = true,
            Address = "http://localhost:8200",
            TokenEnvRef = CustomTokenEnvRef,
        });

        var client = VaultSecretClientFactory.Build(provider);

        client.ShouldNotBeNull();
        client.ShouldBeOfType<VaultClient>();
    }

    [Fact(DisplayName = "Given Enabled=true and the configured token env var unset, when Build runs, then SecretRefUnsetException names the env var (typed failure, not a VaultSharp 403 later)")]
    public void BuildThrowsTypedFailureWhenEnabledAndTokenMissing()
    {
        var provider = BuildServiceProvider(new VaultSecretOptions
        {
            Enabled = true,
            Address = "http://localhost:8200",
            TokenEnvRef = CustomTokenEnvRef,
        });

        var exception = Should.Throw<SecretRefUnsetException>(() => VaultSecretClientFactory.Build(provider));

        exception.Message.ShouldContain(CustomTokenEnvRef);
    }

    [Fact(DisplayName = "Given Enabled=false, when Build runs, then a placeholder client is constructed (the provider short-circuits and never queries it)")]
    public void BuildReturnsPlaceholderClientWhenDisabled()
    {
        var provider = BuildServiceProvider(new VaultSecretOptions { Enabled = false });

        var client = VaultSecretClientFactory.Build(provider);

        client.ShouldNotBeNull();
        client.ShouldBeOfType<VaultClient>();
    }

    /// <summary>Builds a minimal service provider with the two dependencies <c>Build</c> resolves.</summary>
    private static IServiceProvider BuildServiceProvider(VaultSecretOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<ILogger<IVaultClient>>(NullLogger<IVaultClient>.Instance);
        return services.BuildServiceProvider();
    }
}
