using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace Comuki.Host;

/// <summary>
/// Builds the process-wide <see cref="IVaultClient"/> singleton for the
/// Vault secret provider (issue #52, slice 2). The bootstrap token is
/// read from the env var named in <see cref="VaultSecretOptions.TokenEnvRef"/>
/// at startup; failures surface as <see cref="SecretRefUnsetException"/>
/// so the existing <c>ProductionSecretValidator</c> gate (issue #10
/// T11.4) fails the boot with a typed error rather than a VaultSharp
/// 403 on the first resolve. When <see cref="VaultSecretOptions.Enabled"/>
/// is false the client is constructed with a placeholder token — the
/// provider short-circuits on <c>Enabled=false</c> so the placeholder
/// never reaches the wire. Internal (host-assembly) — only
/// <see cref="HostComposer"/> calls into it.
/// </summary>
internal static class VaultSecretClientFactory
{
    /// <summary>
    /// DI factory for <see cref="IVaultClient"/>. Reads the bootstrap
    /// token from <see cref="VaultSecretOptions.TokenEnvRef"/> (env-var
    /// NAME) and constructs a <see cref="VaultClient"/> with
    /// <see cref="TokenAuthMethodInfo"/>. Throws
    /// <see cref="SecretRefUnsetException"/> when Enabled=true and the
    /// env var is unset, so the failure surfaces as a typed error at
    /// resolve time. <c>ProviderExceptionHandler</c> currently has no
    /// dedicated arm for <see cref="SecretRefUnsetException"/> — on the
    /// HTTP surface it falls through to the catch-all 500
    /// <c>internal.error</c> response; an explicit 502
    /// <c>secret_ref_unset</c> mapping is a known follow-up.
    /// </summary>
    /// <param name="serviceProvider">The host's service provider (used to read <see cref="IOptions{TOptions}"/> + <see cref="ILogger{TCategoryName}"/>).</param>
    public static IVaultClient Build(IServiceProvider serviceProvider)
    {
        var optionsAccessor = serviceProvider.GetRequiredService<IOptions<VaultSecretOptions>>();
        var logger = serviceProvider.GetRequiredService<ILogger<IVaultClient>>();
        var optionsValue = optionsAccessor.Value;

        if (!optionsValue.Enabled)
        {
            // The provider short-circuits on Enabled=false (returns null
            // before any Vault call), so the placeholder client is
            // never queried. We use a clearly-unresolvable placeholder
            // address — the literal `http://disabled` is a valid URI
            // shape so VaultSharp's Polymath accepts it without throwing
            // on construction. Any real resolve still goes through the
            // provider's Enabled check.
            var placeholder = new TokenAuthMethodInfo("placeholder-disabled");
            var placeholderSettings = new VaultClientSettings("http://disabled", placeholder);
            return new VaultClient(placeholderSettings);
        }

        var token = Environment.GetEnvironmentVariable(optionsValue.TokenEnvRef);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning(
                "Vault provider is enabled but the bootstrap token env var {TokenEnvRef} is unset; "
                + "refusing to construct the Vault client",
                optionsValue.TokenEnvRef);
            throw new SecretRefUnsetException(optionsValue.TokenEnvRef);
        }

        var authMethod = new TokenAuthMethodInfo(token);
        var settings = new VaultClientSettings(optionsValue.Address, authMethod);
        return new VaultClient(settings);
    }
}
