using Comuki.Engine.Compute.Options;
using Comuki.Host.Auth;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// Startup validator that refuses to boot the host in <c>Production</c>
/// when an obvious default-credentials secret is still present: MinIO
/// keys, bootstrap-admin password, API-key pepper, worker-token pepper,
/// and — since issue #52 — a remote secrets provider that's been
/// enabled but whose bootstrap token env var is unset. The migrator has
/// its own database-password gate
/// (<c>ConnectionStringSource.RejectBlankPasswordInProduction</c>); this
/// one extends the same discipline to the remaining dev-default values
/// (issue #10 T11.4 + security audit A02-1 — production deployments
/// without the env vars were silently booting with public-domain HMAC
/// peppers, allowing anyone to forge keys or tokens). Production-only
/// length / character-class check on the bootstrap-admin password (Q29)
/// keeps operators from deploying with <c>password123</c>.
/// </summary>
public static class ProductionSecretValidator
{
    /// <summary>Minimum password length for the bootstrap admin in <c>Production</c>.</summary>
    public const int BootstrapPasswordMinLength = 12;

    /// <summary>
    /// Inspects the bound options for production-unsafe defaults: MinIO
    /// keys, bootstrap-admin password, API-key pepper, worker-token
    /// pepper, and any enabled remote secrets provider whose token env
    /// is missing. Throws in <c>Production</c> when any of those is still
    /// on its committed dev value — i.e. someone forgot to override the
    /// env var (or config.toml equivalent).
    /// </summary>
    /// <param name="services">The host's service collection (used to read <see cref="IHostEnvironment"/> + <see cref="IConfiguration"/>).</param>
    /// <exception cref="InvalidOperationException">A production-unsafe secret is still on its committed default.</exception>
    public static void Validate(IServiceProvider services)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsProduction())
        {
            return;
        }

        ProductionSecretValidatorExtensions.ValidateMinioSecrets(services);
        ProductionSecretValidatorExtensions.ValidateBootstrapAdmin(services);
        ProductionSecretValidatorExtensions.ValidateApiKeyPepper(services);
        ProductionSecretValidatorExtensions.ValidateWorkerTokenPepper(services);
        ProductionSecretValidatorExtensions.ValidateSecretsProviders(services);
    }
}

/// <summary>
/// Per-secret validation helpers invoked by
/// <see cref="ProductionSecretValidator.Validate"/>. Each method reads
/// one option set via the host's DI and throws when the bound value is
/// still on its committed dev default. Extension-method shape keeps
/// <see cref="ProductionSecretValidator"/> a thin orchestrator with no
/// private members.
/// </summary>
file static class ProductionSecretValidatorExtensions
{
    /// <summary>
    /// Refuses to start the host in <c>Production</c> when the bound
    /// <see cref="ArtifactsOptions.SecretKey"/> still carries the
    /// committed <c>comuki_dev</c> default (matched against the MinIO
    /// root credentials seeded in <c>deploy/compose/docker-compose.yml</c>).
    /// The committed <see cref="ArtifactsOptions.AccessKey"/> default
    /// <c>comuki</c> is also rejected — real deployments use a dedicated
    /// service account name.
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateMinioSecrets(IServiceProvider services)
    {
        var artifacts = services.GetRequiredService<IOptions<ArtifactsOptions>>().Value;

        if (string.Equals(artifacts.SecretKey, "comuki_dev", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "refusing to start the host in Production: Artifacts:Minio:SecretKey is still on its committed dev default "
                + "('comuki_dev'); set the COMUKI_ARTIFACTS_SECRETKEY env var (or the config.toml equivalent) to a strong secret");
        }

        if (string.Equals(artifacts.AccessKey, "comuki", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "refusing to start the host in Production: Artifacts:Minio:AccessKey is still on its committed dev default "
                + "('comuki'); set the COMUKI_ARTIFACTS_ACCESSKEY env var to the dedicated service account name");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when the bootstrap admin
    /// password is still the well-known <c>comuki_dev</c> dev default,
    /// or when it is shorter than
    /// <see cref="ProductionSecretValidator.BootstrapPasswordMinLength"/>
    /// characters, or when it lacks at least one ASCII digit and at
    /// least one non-alphanumeric character (Q29 — weak password check).
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateBootstrapAdmin(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var bootstrap = BootstrapAdminOptions.Resolve(configuration);

        if (bootstrap.AdminPassword is null)
        {
            return;
        }

        if (string.Equals(bootstrap.AdminPassword, "comuki_dev", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to start the host in Production: {BootstrapAdminOptions.PasswordEnvVariable} (or auth:bootstrap:adminPassword) "
                + "is still on its committed dev default ('comuki_dev'); set it to a strong password");
        }

        if (ProductionSecretValidatorHelpers.IsWeakPassword(bootstrap.AdminPassword))
        {
            throw new InvalidOperationException(
                $"refusing to start the host in Production: {BootstrapAdminOptions.PasswordEnvVariable} (or auth:bootstrap:adminPassword) "
                + $"is too weak (must be at least {ProductionSecretValidator.BootstrapPasswordMinLength} characters and contain at least one digit "
                + "and one non-alphanumeric character); set it to a strong password");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when
    /// <see cref="ApiKeyOptions.Pepper"/> still carries its committed
    /// <c>comuki-dev-only-apikey-pepper-override-in-production</c>
    /// default. The pepper is the server-side HMAC-SHA256 key mixed
    /// into every stored key hash; with the dev literal in place, an
    /// attacker who can read the <c>apikeys</c> table can forge any
    /// key's HMAC. Production must set
    /// <see cref="ApiKeyOptions.PepperEnvironmentVariable"/> (security
    /// audit A02-1).
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateApiKeyPepper(IServiceProvider services)
    {
        var apiKey = services.GetRequiredService<IOptions<ApiKeyOptions>>().Value;

        if (string.Equals(apiKey.Pepper, "comuki-dev-only-apikey-pepper-override-in-production", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to start the host in Production: {ApiKeyOptions.PepperEnvironmentVariable} (or {ApiKeyOptions.SectionName}:pepper) "
                + "is still on its committed dev default ('comuki-dev-only-apikey-pepper-override-in-production'); "
                + "set it to a high-entropy random secret");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when
    /// <see cref="WorkerTokenOptions.Pepper"/> still carries its committed
    /// <c>comuki-dev-only-pepper-override-in-production</c> default. The
    /// pepper is the server-side HMAC-SHA256 key mixed into every stored
    /// worker-token hash; with the dev literal in place, an attacker who
    /// can read the <c>worker_tokens</c> table can forge any token's
    /// HMAC. Production must set
    /// <see cref="WorkerTokenOptions.PepperEnvironmentVariable"/>
    /// (security audit A02-1).
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateWorkerTokenPepper(IServiceProvider services)
    {
        var workerToken = services.GetRequiredService<IOptions<WorkerTokenOptions>>().Value;

        if (string.Equals(workerToken.Pepper, "comuki-dev-only-pepper-override-in-production", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"refusing to start the host in Production: {WorkerTokenOptions.PepperEnvironmentVariable} (or {WorkerTokenOptions.SectionName}:pepper) "
                + "is still on its committed dev default ('comuki-dev-only-pepper-override-in-production'); "
                + "set it to a high-entropy random secret");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when a remote secrets
    /// provider is enabled but its bootstrap token env var is unset
    /// on the host. Vault (slice 2) is gated on the env var named by
    /// the configured <see cref="VaultSecretOptions.TokenEnvRef"/> —
    /// the same env var <c>VaultSecretClientFactory</c> reads when
    /// building the client, so a deployment overriding
    /// <c>TokenEnvRef</c> is validated against the variable that is
    /// actually consumed (issue #52 slice-2 audit H1: the gate
    /// previously hard-coded <c>COMUKI_VAULT_TOKEN</c> and drifted
    /// from the configurable factory path). Future token-ful
    /// providers (Consul, slice 3) are checked through
    /// <see cref="SecretsOptions.Providers"/> entries that declare a
    /// token-env-var name.
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateSecretsProviders(IServiceProvider services)
    {
        ValidateVaultBootstrapToken(services);
        ValidateDictionaryProviderBootstrapTokens(services);
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when
    /// <see cref="VaultSecretOptions.Enabled"/> is true but the env
    /// var named by the configured
    /// <see cref="VaultSecretOptions.TokenEnvRef"/> is unset —
    /// exactly the env var <c>VaultSecretClientFactory</c> reads at
    /// bootstrap.
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateVaultBootstrapToken(IServiceProvider services)
    {
        var vault = services.GetRequiredService<IOptions<VaultSecretOptions>>().Value;
        if (!vault.Enabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(vault.TokenEnvRef)))
        {
            throw new InvalidOperationException(
                $"refusing to start the host in Production: the [{VaultSecretOptions.SectionName}] provider is enabled "
                + $"but its bootstrap token env var '{vault.TokenEnvRef}' is unset; set the {vault.TokenEnvRef} env var to a real token");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when an enabled
    /// <see cref="SecretsOptions.Providers"/> entry maps to a
    /// token-ful scheme whose bootstrap env var is unset. Vault is
    /// absent from this path on purpose — it owns
    /// <see cref="VaultSecretOptions"/> and is validated by
    /// <see cref="ValidateVaultBootstrapToken"/> instead.
    /// </summary>
    /// <param name="services"></param>
    public static void ValidateDictionaryProviderBootstrapTokens(IServiceProvider services)
    {
        var secrets = services.GetRequiredService<IOptions<SecretsOptions>>().Value;
        foreach (var (scheme, options) in secrets.Providers)
        {
            if (!options.Enabled)
            {
                continue;
            }

            var tokenEnv = ProductionSecretsHelpers.ProviderTokenEnvName(scheme);
            if (string.IsNullOrWhiteSpace(tokenEnv))
            {
                continue;
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(tokenEnv)))
            {
                throw new InvalidOperationException(
                    $"refusing to start the host in Production: the [{SecretsOptions.SectionName}:{scheme}] provider is enabled "
                    + $"but its bootstrap token env var '{tokenEnv}' is unset; set the {tokenEnv} env var to a real token");
            }
        }
    }
}

/// <summary>
/// File-scoped helpers for the production-secret validator — pure
/// functions, no DI.
/// </summary>
file static class ProductionSecretsHelpers
{
    /// <summary>
    /// Maps a dictionary-provider scheme to the env-var name that
    /// holds the bootstrap token. Vault is deliberately absent — its
    /// gate reads the configurable
    /// <see cref="VaultSecretOptions.TokenEnvRef"/> (see
    /// <see cref="ProductionSecretValidatorExtensions.ValidateVaultBootstrapToken"/>),
    /// not a hard-coded name. Returns <c>null</c> for providers that
    /// do not require a token (env, file, null).
    /// </summary>
    /// <param name="scheme">Lowercase provider scheme.</param>
    public static string? ProviderTokenEnvName(string scheme)
    {
        return scheme switch
        {
            "consul" => "COMUKI_CONSUL_TOKEN",
            _ => null,
        };
    }
}

/// <summary>
/// Pure password-strength predicate used by the bootstrap-admin check.
/// File-scoped — the orchestrator's only consumer. ASCII-only: the
/// check is a deployment gate, not a strength meter.
/// </summary>
file static class ProductionSecretValidatorHelpers
{
    /// <summary>
    /// True when the password is shorter than the configured minimum, or
    /// lacks at least one digit and at least one non-alphanumeric
    /// character.
    /// </summary>
    /// <param name="password">Candidate password.</param>
    public static bool IsWeakPassword(string password)
    {
        if (password.Length < ProductionSecretValidator.BootstrapPasswordMinLength)
        {
            return true;
        }

        var hasDigit = false;
        var hasSymbol = false;
        foreach (var character in password)
        {
            if (char.IsDigit(character))
            {
                hasDigit = true;
            }
            else if (!char.IsLetterOrDigit(character))
            {
                hasSymbol = true;
            }

            if (hasDigit && hasSymbol)
            {
                return false;
            }
        }

        return true;
    }
}
