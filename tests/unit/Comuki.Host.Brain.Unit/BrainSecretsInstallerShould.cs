using Comuki.Host.Brain.Brain;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using VaultSharp;
using VaultSharp.V1.Commons;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// <see cref="BrainSecretsInstaller.AddBrainSecrets"/> tests — the
/// brain host's per-provider DI graph (issue #53, follow-up to the
/// main host's <c>HostComposer</c>). Exercises the wiring end-to-end
/// against a real <see cref="ServiceProvider"/>; the Vault provider
/// receives a substituted <see cref="IVaultClient"/> so the suite
/// stays in-process (no Docker). End-to-end Vault coverage lives in
/// <c>tests/integration/Comuki.Host.Integration.Secrets</c> (the brain
/// shares <see cref="VaultSecretProvider"/> + <see cref="VaultSecretOptions"/>
/// with the main host, so the same Testcontainers fixture covers both).
/// </summary>
public sealed class BrainSecretsInstallerShould : IDisposable
{
    private const string TestTokenEnvVariable = "COMUKI_TEST_BRAIN_VAULT_TOKEN";

    private const string TestEnvVar = "COMUKI_TEST_BRAIN_ENV_VAR";

    public BrainSecretsInstallerShould()
    {
        // Defensive: clear any leftover env-var from a previous run so
        // the Enabled=true + token-set path doesn't pick up a stale value.
        Environment.SetEnvironmentVariable(TestTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(TestEnvVar, null);
    }

    public void Dispose()
    {
        // Mirror the constructor so an exception inside a test does not
        // leak the env var into a sibling class running in parallel.
        Environment.SetEnvironmentVariable(TestTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(TestEnvVar, null);
    }

    [Fact(DisplayName = "Given the installer runs, when the service provider resolves ISecretResolver, then a CompositeSecretResolver answers")]
    public void ResolverRegistersAsComposite()
    {
        var provider = BuildServiceProvider(BuildConfiguration());

        var resolver = provider.GetRequiredService<ISecretResolver>();

        resolver.ShouldBeOfType<CompositeSecretResolver>();
    }

    [Fact(DisplayName = "Given the installer runs, when inspecting IServiceCollection, then env / null / vault / file providers are all registered (CompositeSecretResolver needs every scheme present)")]
    public void AllProvidersAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddBrainSecrets(BuildConfiguration());

        // Inspect the registrations directly before the container is
        // built. CompositeSecretResolver builds its byScheme dictionary
        // from IEnumerable<ISecretProvider> at construction time, so
        // the resolved list at runtime must include every scheme we
        // care about. The Vault registration uses a factory lambda
        // (no ImplementationType); the others are typed. Use
        // ImplementationFactory presence as the "<factory>" marker
        // so the assertion names every concrete type we expect.
        var registeredNames = services
            .Where(static descriptor => descriptor.ServiceType == typeof(ISecretProvider))
            .Select(static descriptor => descriptor.ImplementationType?.Name
                ?? (descriptor.ImplementationFactory is not null ? "<factory>" : "<instance>"))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        registeredNames.ShouldBe(["<factory>", "EnvSecretProvider", "FileSecretProvider", "NullSecretProvider"]);
    }

    [Fact(DisplayName = "Given the installer runs, when the resolver dispatches each parseable scheme, then env / vault / file route to the matching provider (null is parser-blocked — pre-existing)")]
    public async Task ResolverDispatchesEveryParseableSchemeAsync()
    {
        var provider = BuildServiceProvider(BuildConfiguration());
        var resolver = provider.GetRequiredService<ISecretResolver>();

        // env: answered by EnvSecretProvider; a missing env var surfaces
        // as SecretRefUnsetException through the composite (the env
        // provider returns null and the resolver converts that).
        await Should.ThrowAsync<SecretRefUnsetException>(
            () => resolver.ResolveAsync("env:COMUKI_TEST_BRAIN_NO_SUCH_VAR", TestContext.Current.CancellationToken));

        // vault: answered by VaultSecretProvider (Enabled=false →
        // provider short-circuits → composite throws
        // SecretRefUnsetException). The secret exception rather than
        // format exception proves the provider is wired.
        await Should.ThrowAsync<SecretRefUnsetException>(
            () => resolver.ResolveAsync("vault:test#key", TestContext.Current.CancellationToken));

        // file: answered by FileSecretProvider (Enabled=false →
        // short-circuits → composite throws SecretRefUnsetException).
        await Should.ThrowAsync<SecretRefUnsetException>(
            () => resolver.ResolveAsync("file:/tmp/comuki-no-such-file", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given env:SOME_VAR set, when the resolver runs, then the env provider answers (no regression on existing brain deployments)")]
    public async Task EnvRefStillResolvesAfterVaultWiringAsync()
    {
        Environment.SetEnvironmentVariable(TestEnvVar, "from-env");
        var provider = BuildServiceProvider(BuildConfiguration());

        var resolved = await provider.GetRequiredService<ISecretResolver>()
            .ResolveAsync($"env:{TestEnvVar}", TestContext.Current.CancellationToken);

        resolved.ShouldBe("from-env");
        Environment.SetEnvironmentVariable(TestEnvVar, null);
    }

    [Fact(DisplayName = "Given a vault ref and Enabled=false (default), when the resolver runs, then SecretRefUnsetException surfaces — the provider short-circuits, Vault is never contacted")]
    public async Task VaultRefUnsetWhenProviderDisabledAsync()
    {
        var vaultClient = Substitute.For<IVaultClient>();
        var provider = BuildServiceProvider(
            BuildConfiguration(),
            vaultClient: vaultClient);

        await Should.ThrowAsync<SecretRefUnsetException>(
            () => provider.GetRequiredService<ISecretResolver>()
                .ResolveAsync("vault:models/brain#endpoint", TestContext.Current.CancellationToken));

        // The provider short-circuits on Enabled=false; VaultSharp must
        // never see the read chain.
        await vaultClient.V1.Secrets.KeyValue.V2
            .DidNotReceive()
            .ReadSecretAsync(path: Arg.Any<string>(), mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
    }

    [Fact(DisplayName = "Given a vault ref and Enabled=true with a token, when the resolver runs, then the substituted VaultClient answers and the field value flows through")]
    public async Task VaultRefResolvesViaSubstituteClientAsync()
    {
        const string SecretPath = "models/brain";
        const string SecretField = "endpoint";
        const string SecretValue = "https://live/v4";

        var vaultClient = Substitute.For<IVaultClient>();
        var secret = new Secret<SecretData>
        {
            Data = new SecretData { Data = new Dictionary<string, object> { [SecretField] = SecretValue } },
        };
        vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: SecretPath, mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>())
            .Returns(secret);

        Environment.SetEnvironmentVariable(TestTokenEnvVariable, "brain-test-bootstrap-token");
        try
        {
            var provider = BuildServiceProvider(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Secrets:Vault:Enabled"] = "true",
                        ["Secrets:Vault:Address"] = "http://vault.test.local:8200",
                        ["Secrets:Vault:TokenEnvRef"] = TestTokenEnvVariable,
                        ["Secrets:Vault:KvMount"] = "secret",
                    })
                    .Build(),
                vaultClient: vaultClient);

            var resolved = await provider.GetRequiredService<ISecretResolver>()
                .ResolveAsync($"vault:{SecretPath}#{SecretField}", TestContext.Current.CancellationToken);

            resolved.ShouldBe(SecretValue);
            await vaultClient.V1.Secrets.KeyValue.V2
                .Received(1)
                .ReadSecretAsync(path: SecretPath, mountPoint: Arg.Any<string?>(), wrapTimeToLive: Arg.Any<string?>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestTokenEnvVariable, null);
        }
    }

    [Fact(DisplayName = "Given a vault ref with Enabled=true but no token, when the IVaultClient is resolved from the container, then SecretRefUnsetException names the env var (fail-fast, not a VaultSharp 403 later)")]
    public Task VaultClientFactoryFailsFastWhenTokenMissingAsync()
    {
        // Default TokenEnvRef is COMUKI_VAULT_TOKEN — explicitly clear so
        // the factory's `unset env var` branch fires.
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);

        var provider = BuildServiceProvider(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:Vault:Enabled"] = "true",
                    ["Secrets:Vault:Address"] = "http://vault.test.local:8200",
                })
                .Build());

        var exception = Should.Throw<SecretRefUnsetException>(provider.GetRequiredService<IVaultClient>);

        exception.Message.ShouldContain(VaultSecretOptions.DefaultTokenEnvVariable);
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Given the [Secrets:Vault] section bound from configuration, when IOptions is materialised, then every option carries through")]
    public void VaultOptionsBindFromConfiguration()
    {
        var provider = BuildServiceProvider(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:Vault:Enabled"] = "true",
                    ["Secrets:Vault:Address"] = "http://vault.cluster.local:8200",
                    ["Secrets:Vault:TokenEnvRef"] = TestTokenEnvVariable,
                    ["Secrets:Vault:KvMount"] = "comuki",
                })
                .Build());

        var options = provider.GetRequiredService<IOptions<VaultSecretOptions>>().Value;

        options.Enabled.ShouldBeTrue();
        options.Address.ShouldBe("http://vault.cluster.local:8200");
        options.TokenEnvRef.ShouldBe(TestTokenEnvVariable);
        options.KvMount.ShouldBe("comuki");
        // CacheTtl keeps its DefaultCacheTtl when nothing overrides it.
        options.CacheTtl.ShouldBe(VaultSecretOptions.DefaultCacheTtl);
    }

    [Fact(DisplayName = "Given the [Secrets:Vault] section with Enabled=false and a missing Address, when the service provider builds, then ValidateOnStart does not reject the disabled config (the dev / single-container path stays bootable)")]
    public void VaultOptionsAcceptDisabledWithoutAddress()
    {
        // This is the dev / single-container brain install — no Vault
        // configured. The validator short-circuits on Enabled=false so
        // BuildServiceProvider must succeed even without Address.
        var provider = BuildServiceProvider(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:Vault:Enabled"] = "false",
                })
                .Build());

        var options = provider.GetRequiredService<IOptions<VaultSecretOptions>>().Value;

        options.Enabled.ShouldBeFalse();
        options.Address.ShouldBe(string.Empty);
    }

    /// <summary>Builds a real <see cref="ServiceProvider"/> with the installer applied. The caller can swap <paramref name="vaultClient"/> for a substituted one after the installer runs.</summary>
    /// <param name="configuration">Configuration passed to <see cref="BrainSecretsInstaller.AddBrainSecrets"/>.</param>
    /// <param name="vaultClient">Optional <see cref="IVaultClient"/> substitute; when null, the installerregisters the factory and a placeholder VaultClient is built lazily.</param>
    private static ServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        IVaultClient? vaultClient = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddBrainSecrets(configuration);
        if (vaultClient is not null)
        {
            services.RemoveAll<IVaultClient>();
            services.AddSingleton(vaultClient);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Empty <see cref="IConfiguration"/> (Vault defaults to Enabled=false).</summary>
    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder().Build();
    }
}
