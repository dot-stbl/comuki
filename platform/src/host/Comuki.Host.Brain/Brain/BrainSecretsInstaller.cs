using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Wires the brain host's <see cref="ISecretResolver"/> chain
/// (issue #53, follow-up to the host wiring in <c>Comuki.Host.HostComposer</c>).
/// The brain is a separate process — it needs its own copy of the
/// per-provider registrations that <c>VaultSecretClientFactory</c> /
/// <c>FileSecretProvider</c> / <c>VaultSecretProvider</c> / the composite
/// resolver rely on, but the Brain host doesn't need (and intentionally
/// skips) the production-secret / MinIO / OIDC validation gate that the
/// main host runs through <c>ProductionSecretValidator</c>. The Vault
/// provider gracefully short-circuits when <see cref="VaultSecretOptions.Enabled"/>
/// is false, so an unconfigured Brain deployment (developer setups,
/// single-container installs) keeps its existing
/// "first think call fails with a setup hint" behaviour — <c>vault:</c>
/// refs simply surface as <see cref="SecretRefUnsetException"/> through
/// the composite rather than <see cref="SecretRefFormatException"/> from
/// the missing-provider path.
/// </summary>
public static class BrainSecretsInstaller
{
    /// <summary>
    /// Extension entry point. Matches the <c>HostComposer.Compose</c>
    /// ordering: root <see cref="SecretsOptions"/> + per-provider sub-
    /// sections (<see cref="FileSecretOptions"/>, <see cref="VaultSecretOptions"/>)
    /// bound first with <c>ValidateOnStart</c>; the composite resolver
    /// and every <see cref="ISecretProvider"/> are registered
    /// unconditionally; remote providers self-gate on their <c>Enabled</c>
    /// flag so a Brain deployment without Vault keeps working.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's <see cref="IConfiguration"/> (env, TOML, appsettings).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBrainSecrets(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Root secrets options (issue #52, slice 1): the section is
        // opt-in — per-provider sub-sections default to
        // Enabled = false. Bound with ValidateOnStart so a misconfigured
        // provider fails the boot, not the first request.
        services.AddOptions<SecretsOptions>()
            .Bind(configuration.GetSection(SecretsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // FileSecretOptions bound separately under [Secrets:File] so
        // FileSecretProvider can enforce the RootPath allowlist. Same
        // pattern as the main host (issue #52 slice-1 audit H1).
        services.AddOptions<FileSecretOptions>()
            .Bind(configuration.GetSection(FileSecretOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Vault (issue #52, slice 2): [Secrets:Vault]:Enabled gates the
        // IVaultClient bootstrap. When Enabled=true the factory reads
        // the bootstrap token from the env var named in
        // VaultSecretOptions.TokenEnvRef at startup and bakes it into
        // the VaultSharp VaultClient (TokenAuthMethodInfo). When
        // Enabled=false the factory uses a placeholder token —
        // VaultSecretProvider's ResolveAsync short-circuits on
        // Enabled=false so the client is never actually used. The brain
        // host does not run the main host's
        // ProductionSecretValidator.ValidateVaultBootstrapToken gate
        // (Brain is not a primary web host; the operator runs the
        // brain as a sidecar in production and trusts the deployment's
        // secret-provisioning contract). Memory cache backs the TTL
        // cache (issue #52 §Design — remote default 60s).
        services.AddOptions<VaultSecretOptions>()
            .Bind(configuration.GetSection(VaultSecretOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<VaultSecretOptions>, VaultSecretOptionsValidator>();
        services.AddMemoryCache();
        services.AddSingleton(BrainVaultSecretClientFactory.Build);
        services.AddSingleton<ISecretProvider>(static serviceProvider =>
            new VaultSecretProvider(
                serviceProvider.GetRequiredService<IOptions<VaultSecretOptions>>(),
                serviceProvider.GetRequiredService<IVaultClient>(),
                serviceProvider.GetRequiredService<ILogger<VaultSecretProvider>>(),
                serviceProvider.GetRequiredService<IMemoryCache>()));

        // Composite resolver + always-on providers. Env covers bare
        // names and env:*; null is the explicit fallback when no
        // provider is configured for a scheme (issue #52, slice 1).
        // The file provider is registered unconditionally and
        // self-gates on FileSecretOptions.Enabled — short-circuits to
        // null before any filesystem access, so a file:/path ref in a
        // disabled deployment surfaces as SecretRefUnsetException
        // through the composite rather than reading the host blindly.
        services.AddSingleton<ISecretResolver, CompositeSecretResolver>();
        services.AddSingleton<ISecretProvider, EnvSecretProvider>();
        services.AddSingleton<ISecretProvider, NullSecretProvider>();
        services.AddSingleton<ISecretProvider, FileSecretProvider>();

        return services;
    }
}

/// <summary>
/// Builds the brain host's process-wide <see cref="IVaultClient"/>
/// singleton (issue #52, slice 2; brain host equivalent of
/// <c>Comuki.Host.VaultSecretClientFactory</c>). The bootstrap token
/// is read from the env var named in
/// <see cref="VaultSecretOptions.TokenEnvRef"/> at startup; failures
/// surface as <see cref="SecretRefUnsetException"/> so the brain host
/// fails fast with a typed error rather than a VaultSharp 403 on the
/// first <c>vault:</c> resolve. When <see cref="VaultSecretOptions.Enabled"/>
/// is false the client is constructed with a placeholder token — the
/// provider short-circuits on <c>Enabled=false</c>, so the placeholder
/// never reaches the wire. <c>internal</c> + file-scoped — only
/// <see cref="BrainSecretsInstaller.AddBrainSecrets"/> calls into it
/// (Brain has no Vault integration tests yet; the main host owns the
/// end-to-end Vault fixture in
/// <c>tests/integration/Comuki.Host.Integration.Secrets</c>).
/// </summary>
file static class BrainVaultSecretClientFactory
{
    /// <summary>
    /// DI factory for <see cref="IVaultClient"/>. Reads the bootstrap
    /// token from <see cref="VaultSecretOptions.TokenEnvRef"/> (env-var
    /// NAME, not value) and constructs a <see cref="VaultClient"/> with
    /// <see cref="TokenAuthMethodInfo"/>. Throws
    /// <see cref="SecretRefUnsetException"/> when <see cref="VaultSecretOptions.Enabled"/>
    /// is true and the env var is unset, so the failure surfaces as a
    /// typed error at startup rather than a generic VaultSharp
    /// exception later.
    /// </summary>
    /// <param name="serviceProvider">The host's service provider (reads <see cref="IOptions{TOptions}"/> + <see cref="ILogger{TCategoryName}"/>).</param>
    public static IVaultClient Build(IServiceProvider serviceProvider)
    {
        var optionsAccessor = serviceProvider.GetRequiredService<IOptions<VaultSecretOptions>>();
        var logger = serviceProvider.GetRequiredService<ILogger<IVaultClient>>();
        var optionsValue = optionsAccessor.Value;

        if (!optionsValue.Enabled)
        {
            // The provider short-circuits on Enabled=false (returns
            // null before any Vault call), so the placeholder client
            // is never queried. The literal `http://disabled` is a
            // valid URI shape so VaultSharp's Polymath accepts it
            // without throwing on construction. Any real resolve
            // still goes through the provider's Enabled check.
            var placeholder = new TokenAuthMethodInfo("placeholder-disabled");
            var placeholderSettings = new VaultClientSettings("http://disabled", placeholder);
            return new VaultClient(placeholderSettings);
        }

        var token = Environment.GetEnvironmentVariable(optionsValue.TokenEnvRef);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning(
                "Vault provider is enabled in the brain host but the bootstrap token env var {TokenEnvRef} is unset; "
                + "refusing to construct the Vault client",
                optionsValue.TokenEnvRef);
            throw new SecretRefUnsetException(optionsValue.TokenEnvRef);
        }

        var authMethod = new TokenAuthMethodInfo(token);
        var settings = new VaultClientSettings(optionsValue.Address, authMethod);
        return new VaultClient(settings);
    }
}
