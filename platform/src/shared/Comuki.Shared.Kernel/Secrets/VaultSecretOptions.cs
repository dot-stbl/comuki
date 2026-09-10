using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Options for the HashiCorp Vault secret provider (issue #52, slice 2).
/// Bound from <c>[Secrets:Vault]</c> in TOML / <c>Secrets:Vault:*</c> in
/// appsettings. <see cref="Enabled"/> gates the provider registration —
/// when false, the factory in <c>HostComposer</c> still wires the client
/// (cheap, no I/O at boot) but <see cref="VaultSecretProvider"/>'s
/// <c>ResolveAsync</c> short-circuits to <c>null</c>, so a <c>vault:</c>
/// reference surfaces as a typed <see cref="SecretRefUnsetException"/>
/// rather than spending a Vault round-trip. <see cref="TokenEnvRef"/>
/// names the env var that carries the bootstrap Vault token — the token
/// value itself is never in this config (per
/// <c>configuration-toml-env.md</c>: secrets in env, never in the file).
/// <see cref="KvMount"/> is the KV v2 mount point (the path prefix
/// behind which all secret paths live — typically <c>secret</c>).
/// <see cref="CacheTtl"/> is the in-process TTL for resolved values so
/// Vault-side rotation picks up within one TTL without a restart
/// (issue #52 §Design — remote default 60s).
/// </summary>
public sealed class VaultSecretOptions
{
    /// <summary>Configuration section name (<c>[Secrets:Vault]</c> in TOML / <c>Secrets:Vault:*</c> in appsettings).</summary>
    public const string SectionName = "Secrets:Vault";

    /// <summary>Env-var name that holds the Vault bootstrap token (the token itself is in env, never here).</summary>
    public const string DefaultTokenEnvVariable = "COMUKI_VAULT_TOKEN";

    /// <summary>Default K/V v2 mount point (<c>secret</c>).</summary>
    public const string DefaultKvMount = "secret";

    /// <summary>Default cache TTL for resolved values (remote providers default to 60s per issue #52 §Design).</summary>
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Master switch — when false, <see cref="VaultSecretProvider"/> short-circuits to <c>null</c>
    /// without contacting Vault. The <see cref="VaultSharp.VaultClient"/> itself is still constructed
    /// (cheap, lazy login) so wiring is identical across environments.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Vault server URL (e.g. <c>https://vault.svc.cluster.local:8200</c>). Validated by
    /// <see cref="VaultSecretOptionsValidator"/> only when <see cref="Enabled"/> is true;
    /// a disabled deployment can ship with an empty <see cref="Address"/> (the factory uses
    /// a placeholder client that never reaches the wire).
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Env-var NAME (not value) that holds the bootstrap Vault token. Default
    /// <c>COMUKI_VAULT_TOKEN</c> matches the production-secret gate
    /// (<c>ProductionSecretValidatorExtensions.ValidateSecretsProviders</c>).
    /// Validated only when <see cref="Enabled"/> is true.
    /// </summary>
    public string TokenEnvRef { get; init; } = DefaultTokenEnvVariable;

    /// <summary>K/V v2 mount point. Default <c>secret</c>; operators override when their cluster uses a non-standard mount.</summary>
    public string KvMount { get; init; } = DefaultKvMount;

    /// <summary>In-process TTL for resolved secret values. Zero disables the cache (every resolve hits Vault).</summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan CacheTtl { get; init; } = DefaultCacheTtl;
}

/// <summary>
/// Validator for <see cref="VaultSecretOptions"/> that skips the
/// <see cref="Required"/> + <see cref="Url"/> checks when the provider is
/// disabled. The disabled path is the common case in deployments that
/// ship only env/file refs — running the full OpenAPI document capture
/// (which builds the host in a stub environment without any
/// <c>[Secrets:Vault]</c> configuration) must not fail on
/// <c>Address is required</c>. Registered by <c>HostComposer</c> as an
/// <see cref="IValidateOptions{TOptions}"/>; <c>public</c> so the host
/// composition (cross-assembly) can register it.
/// </summary>
public sealed class VaultSecretOptionsValidator : IValidateOptions<VaultSecretOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VaultSecretOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Address))
        {
            failures.Add("Address is required when Vault is enabled");
        }
        else if (!Uri.TryCreate(options.Address, UriKind.Absolute, out var addressUri)
            || (addressUri.Scheme != Uri.UriSchemeHttp && addressUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"Address '{options.Address}' is not a valid http/https URL");
        }

        if (string.IsNullOrWhiteSpace(options.TokenEnvRef))
        {
            failures.Add("TokenEnvRef is required when Vault is enabled (env-var name, not value)");
        }

        if (string.IsNullOrWhiteSpace(options.KvMount))
        {
            failures.Add("KvMount is required when Vault is enabled");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
