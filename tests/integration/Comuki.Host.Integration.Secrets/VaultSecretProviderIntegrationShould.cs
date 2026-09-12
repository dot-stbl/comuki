using Comuki.Shared.Kernel.Secrets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using Xunit;

namespace Comuki.Host.Integration.Secrets;

/// <summary>
/// VaultSecretProvider end-to-end tests against a real Hashicorp Vault
/// server (issue #52, slice 2). The official Testcontainers module for
/// Vault (<c>Testcontainers.HashicorpVault</c>) is not on NuGet at the
/// time of this slice — only the <c>LowkeyVault</c> mock module is
/// published — so the fixture spins the official
/// <c>hashicorp/vault:1.15</c> image in dev mode via
/// <see cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}"/>.
/// All tests skip gracefully when Docker is unavailable on the host
/// (no <c>docker</c> on PATH, no DOCKER_HOST, etc.); see
/// <see cref="VaultFixture"/> for the skip-on-no-Docker detection and
/// <see cref="Assert.SkipUnless(bool, string)"/> in every test.
/// </summary>
public sealed class VaultSecretProviderIntegrationShould(VaultSecretProviderIntegrationShould.VaultFixture fixture) : IClassFixture<VaultSecretProviderIntegrationShould.VaultFixture>
{
    private const string Image = "hashicorp/vault:1.15";

    private const string KvMount = "secret";

    private const string SecretPath = "prod/db";

    private const string SecretField = "password";

    private const string SecretValue = "s3cr3t";

    private readonly VaultFixture fixture = fixture;

    [Fact(DisplayName = "Given a real Vault server with a K/V v2 secret, when ResolveAsync runs, then the field value is returned")]
    public async Task ResolveAsyncReadsSecretFromRealVaultAsync()
    {
        Assert.SkipUnless(fixture.VaultAvailable, "Vault integration test requires Docker; not available on this host.");

        await fixture.WriteSecretValueAsync(SecretValue);
        var provider = BuildProvider(fixture);

        var resolved = await provider.ResolveAsync(
            new SecretRef("vault", SecretPath, SecretField),
            TestContext.Current.CancellationToken);

        resolved.ShouldBe(SecretValue);
    }

    [Fact(DisplayName = "Given the same resolve twice within the TTL, when both runs complete, then Vault is hit only once (cache front)")]
    public async Task ResolveAsyncCachesAcrossCallsAsync()
    {
        Assert.SkipUnless(fixture.VaultAvailable, "Vault integration test requires Docker; not available on this host.");

        await fixture.WriteSecretValueAsync(SecretValue);
        var provider = BuildProvider(fixture);

        var first = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);
        var second = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);

        first.ShouldBe(SecretValue);
        second.ShouldBe(SecretValue);

        // After cache-hit on the second call, mutate the underlying
        // value via Vault directly — the cached entry must still
        // serve the original value.
        await fixture.WriteSecretValueAsync("mutated");
        var third = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);
        third.ShouldBe(SecretValue);
    }

    [Fact(DisplayName = "Given a rotated Vault value after the 1s cache TTL elapses, when ResolveAsync runs again, then the fresh value is observed (rotation picks up within one TTL)")]
    public async Task ResolveAsyncObservesRotatedValueAfterTtlExpiryAsync()
    {
        Assert.SkipUnless(fixture.VaultAvailable, "Vault integration test requires Docker; not available on this host.");

        await fixture.WriteSecretValueAsync(SecretValue);
        var provider = BuildProvider(fixture, cacheTtl: TimeSpan.FromSeconds(1));

        var beforeRotation = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);

        await fixture.WriteSecretValueAsync("rotated-fresh-value");
        // TTL is 1s — wait past it so the cache entry expires and the
        // next resolve must hit Vault again (issue #52 acceptance §6:
        // rotation picks up within one TTL, no restart). The bounded
        // delay is deliberate: the memory-cache expiry is
        // wall-clock-driven, not observable otherwise.
        await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);

        var afterRotation = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);

        beforeRotation.ShouldBe(SecretValue);
        afterRotation.ShouldBe("rotated-fresh-value");
    }

    [Fact(DisplayName = "Given Enabled=false, when ResolveAsync runs against a real Vault, then the resolve short-circuits without contacting the server")]
    public async Task ResolveAsyncShortCircuitsWhenDisabledAsync()
    {
        Assert.SkipUnless(fixture.VaultAvailable, "Vault integration test requires Docker; not available on this host.");

        var provider = BuildProvider(fixture, enabled: false);

        var resolved = await provider.ResolveAsync(new SecretRef("vault", SecretPath, SecretField), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    /// <summary>Builds the provider under test against the fixture's VaultClient + token + endpoint.</summary>
    private static VaultSecretProvider BuildProvider(VaultFixture fixture, bool enabled = true, TimeSpan? cacheTtl = null)
    {
        VaultSecretOptions options = new()
        {
            Enabled = enabled,
            Address = fixture.VaultAddress,
            TokenEnvRef = VaultFixture.TokenEnvVariable,
            KvMount = KvMount,
            CacheTtl = cacheTtl ?? TimeSpan.FromSeconds(60),
        };

        return new VaultSecretProvider(
            Options.Create(options),
            fixture.VaultClient,
            NullLogger<VaultSecretProvider>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    /// <summary>
    /// Hashicorp Vault fixture (Testcontainers). Boots the official
    /// <c>hashicorp/vault:1.15</c> image in dev mode (which auto-mounts
    /// the K/V v2 secrets engine at <c>secret/</c>), writes a test secret,
    /// and constructs the <see cref="IVaultClient"/> the production
    /// provider shares. <see cref="VaultAvailable"/> is <c>false</c> when
    /// Docker is not reachable on this host — the integration test class
    /// calls <see cref="Assert.SkipUnless(bool, string)"/> to
    /// skip every test in that case so the suite reports a clean
    /// "skipped" instead of a failed run.
    /// </summary>
    public sealed class VaultFixture : IAsyncLifetime
    {
        /// <summary>Env var name the production factory reads for the bootstrap token. Set in <see cref="InitializeAsync"/>.</summary>
        public const string TokenEnvVariable = "COMUKI_VAULT_TOKEN";

        /// <summary>True when Docker was reachable AND the container started AND we wrote the test secret.</summary>
        public bool VaultAvailable { get; private set; }

        public string VaultAddress { get; private set; } = string.Empty;

        public IVaultClient VaultClient { get; private set; } = null!;

        private IContainer? container;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            try
            {
                container = new ContainerBuilder(Image)
                    .WithName($"comuki-vault-{Guid.NewGuid():N}")
                    .WithPortBinding(8200, true)
                    .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", "comuki-root-token")
                    .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilMessageIsLogged("Vault server started"))
                    .Build();

                await container.StartAsync(TestContext.Current.CancellationToken);

                // On non-Linux testcontainers hosts (or when no Docker
                // socket-provider is wired), GetConnectionString() can
                // throw ConnectionStringProviderNotConfiguredException;
                // resolve the address manually via Hostname +
                // GetMappedPublicPort(8200) so the test does not depend
                // on a docker-host-specific connection string provider.
                var hostname = container.Hostname;
                var port = container.GetMappedPublicPort(8200);
                VaultAddress = $"http://{hostname}:{port}";

                Environment.SetEnvironmentVariable(TokenEnvVariable, "comuki-root-token");

                VaultClient = new VaultClient(new VaultClientSettings(
                    VaultAddress,
                    new TokenAuthMethodInfo("comuki-root-token")));

                await WriteSecretValueAsync(SecretValue);
                VaultAvailable = true;
            }
            catch (Exception ex)
            {
                VaultAvailable = false;
                Console.WriteLine($"[VaultFixture] Docker unavailable, skipping integration tests: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            Environment.SetEnvironmentVariable(TokenEnvVariable, null);
        }

        /// <summary>Writes (or overwrites) the test secret value through the K/V v2 engine.</summary>
        public async Task WriteSecretValueAsync(string value)
        {
            await VaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                path: SecretPath,
                data: new Dictionary<string, object> { [SecretField] = value },
                mountPoint: KvMount);
        }
    }
}
