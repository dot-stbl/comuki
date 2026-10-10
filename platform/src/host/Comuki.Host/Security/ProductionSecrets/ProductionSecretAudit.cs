using Comuki.Engine.Compute.Options;
using Comuki.Host.Auth;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// Collects the production-secret findings without throwing: every check
/// the startup gate runs (issue #10 T11.4, security audits A02-1, issue
/// #52) evaluated into <see cref="ProductionSecretFinding"/> entries.
/// <c>ProductionSecretValidator</c> turns the Fail entries into the boot
/// refusal; <c>comuki doctor</c> (issue #56) prints them as a checklist.
/// Outside Production the same dev defaults surface as Warn — informative,
/// not fatal.
/// </summary>
public static class ProductionSecretAudit
{
    /// <summary>Runs every secret check and returns the findings in gate order.</summary>
    /// <param name="services">Provider exposing IHostEnvironment, IConfiguration and the bound secret-bearing options.</param>
    public static IReadOnlyList<ProductionSecretFinding> Collect(IServiceProvider services)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var isProduction = environment.IsProduction();
        var findings = new List<ProductionSecretFinding>();

        ProductionSecretAuditChecks.CollectMinio(services, isProduction, findings);
        ProductionSecretAuditChecks.CollectBootstrapAdmin(services, isProduction, findings);
        ProductionSecretAuditChecks.CollectApiKeyPepper(services, isProduction, findings);
        ProductionSecretAuditChecks.CollectWorkerTokenPepper(services, isProduction, findings);
        ProductionSecretAuditChecks.CollectSecretsProviders(services, isProduction, findings);

        return findings;
    }
}

/// <summary>The individual production-secret checks; each appends its findings to the shared list.</summary>
file static class ProductionSecretAuditChecks
{
    /// <summary>Audits the MinIO artifact-store access and secret keys against the dev-default gate.</summary>
    public static void CollectMinio(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var artifacts = services.GetRequiredService<IOptions<ArtifactsOptions>>().Value;

        if (string.Equals(artifacts.SecretKey, "comuki_dev", StringComparison.Ordinal))
        {
            findings.Add(FailOrWarn(
                isProduction,
                "minio.secretkey",
                "refusing to start the host in Production: Artifacts:Minio:SecretKey is still on its committed dev default "
                + "('comuki_dev'); set the COMUKI_ARTIFACTS_SECRETKEY env var (or the config.toml equivalent) to a strong secret"));
        }
        else
        {
            findings.Add(new ProductionSecretFinding("minio.secretkey", ProductionSecretFinding.Severity.Ok, "Artifacts:Minio:SecretKey is set"));
        }

        if (string.Equals(artifacts.AccessKey, "comuki", StringComparison.Ordinal))
        {
            findings.Add(FailOrWarn(
                isProduction,
                "minio.accesskey",
                "refusing to start the host in Production: Artifacts:Minio:AccessKey is still on its committed dev default "
                + "('comuki'); set the COMUKI_ARTIFACTS_ACCESSKEY env var to the dedicated service account name"));
        }
        else
        {
            findings.Add(new ProductionSecretFinding("minio.accesskey", ProductionSecretFinding.Severity.Ok, "Artifacts:Minio:AccessKey is set"));
        }
    }

    /// <summary>Audits the bootstrap-admin password: unset ok, dev default or weak → fail-or-warn.</summary>
    public static void CollectBootstrapAdmin(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var bootstrap = BootstrapAdminOptions.Resolve(configuration);

        if (bootstrap.AdminPassword is null)
        {
            findings.Add(new ProductionSecretFinding(
                "bootstrap-admin",
                ProductionSecretFinding.Severity.Ok,
                "bootstrap admin disabled (no password configured)"));
            return;
        }

        if (string.Equals(bootstrap.AdminPassword, "comuki_dev", StringComparison.Ordinal))
        {
            findings.Add(FailOrWarn(
                isProduction,
                "bootstrap-admin",
                $"refusing to start the host in Production: {BootstrapAdminOptions.PasswordEnvVariable} (or auth:bootstrap:adminPassword) "
                + "is still on its committed dev default ('comuki_dev'); set it to a strong password"));
            return;
        }

        if (ProductionSecretAuditRules.IsWeakPassword(bootstrap.AdminPassword))
        {
            findings.Add(FailOrWarn(
                isProduction,
                "bootstrap-admin",
                $"refusing to start the host in Production: {BootstrapAdminOptions.PasswordEnvVariable} (or auth:bootstrap:adminPassword) "
                + $"is too weak (must be at least {ProductionSecretValidator.BootstrapPasswordMinLength} characters and contain at least one digit "
                + "and one non-alphanumeric character); set it to a strong password"));
            return;
        }

        findings.Add(new ProductionSecretFinding(
            "bootstrap-admin",
            ProductionSecretFinding.Severity.Ok,
            "bootstrap admin password is set and passes the strength check"));
    }

    /// <summary>Audits the identity API-key pepper against the dev-default gate.</summary>
    public static void CollectApiKeyPepper(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var apiKey = services.GetRequiredService<IOptions<ApiKeyOptions>>().Value;

        findings.Add(string.Equals(apiKey.Pepper, "comuki-dev-only-apikey-pepper-override-in-production", StringComparison.Ordinal)
            ? FailOrWarn(
                isProduction,
                "apikey-pepper",
                $"refusing to start the host in Production: {ApiKeyOptions.PepperEnvironmentVariable} (or {ApiKeyOptions.SectionName}:pepper) "
                + "is still on its committed dev default ('comuki-dev-only-apikey-pepper-override-in-production'); "
                + "set it to a high-entropy random secret")
            : new ProductionSecretFinding(
                "apikey-pepper",
                ProductionSecretFinding.Severity.Ok,
                $"{ApiKeyOptions.PepperEnvironmentVariable} is set"));
    }

    /// <summary>Audits the worker-token pepper against the dev-default gate.</summary>
    public static void CollectWorkerTokenPepper(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var workerToken = services.GetRequiredService<IOptions<WorkerTokenOptions>>().Value;

        findings.Add(string.Equals(workerToken.Pepper, "comuki-dev-only-pepper-override-in-production", StringComparison.Ordinal)
            ? FailOrWarn(
                isProduction,
                "worker-token-pepper",
                $"refusing to start the host in Production: {WorkerTokenOptions.PepperEnvironmentVariable} (or {WorkerTokenOptions.SectionName}:pepper) "
                + "is still on its committed dev default ('comuki-dev-only-pepper-override-in-production'); "
                + "set it to a high-entropy random secret")
            : new ProductionSecretFinding(
                "worker-token-pepper",
                ProductionSecretFinding.Severity.Ok,
                $"{WorkerTokenOptions.PepperEnvironmentVariable} is set"));
    }

    /// <summary>Runs the Vault and dictionary-provider token audits.</summary>
    public static void CollectSecretsProviders(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        CollectVault(services, isProduction, findings);
        CollectDictionaryProviders(services, isProduction, findings);
    }

    /// <summary>Audits the Vault provider's bootstrap token when the provider is enabled.</summary>
    public static void CollectVault(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var vault = services.GetRequiredService<IOptions<VaultSecretOptions>>().Value;
        if (!vault.Enabled)
        {
            findings.Add(new ProductionSecretFinding(
                "secrets.vault",
                ProductionSecretFinding.Severity.Ok,
                "vault provider disabled"));
            return;
        }

        findings.Add(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(vault.TokenEnvRef))
            ? FailOrWarn(
                isProduction,
                "secrets.vault",
                $"refusing to start the host in Production: the [{VaultSecretOptions.SectionName}] provider is enabled "
                + $"but its bootstrap token env var '{vault.TokenEnvRef}' is unset; set the {vault.TokenEnvRef} env var to a real token")
            : new ProductionSecretFinding(
                "secrets.vault",
                ProductionSecretFinding.Severity.Ok,
                $"vault provider enabled, bootstrap token env var '{vault.TokenEnvRef}' is set"));
    }

    /// <summary>Audits every enabled dictionary provider that requires a bootstrap-token env var.</summary>
    public static void CollectDictionaryProviders(IServiceProvider services, bool isProduction, List<ProductionSecretFinding> findings)
    {
        var secrets = services.GetRequiredService<IOptions<SecretsOptions>>().Value;
        foreach (var (scheme, options) in secrets.Providers)
        {
            if (!options.Enabled)
            {
                continue;
            }

            var tokenEnv = ProductionSecretAuditRules.ProviderTokenEnvName(scheme);
            if (string.IsNullOrWhiteSpace(tokenEnv))
            {
                continue;
            }

            findings.Add(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(tokenEnv))
                ? FailOrWarn(
                    isProduction,
                    $"secrets.{scheme}",
                    $"refusing to start the host in Production: the [{SecretsOptions.SectionName}:{scheme}] provider is enabled "
                    + $"but its bootstrap token env var '{tokenEnv}' is unset; set the {tokenEnv} env var to a real token")
                : new ProductionSecretFinding(
                    $"secrets.{scheme}",
                    ProductionSecretFinding.Severity.Ok,
                    $"provider '{scheme}' enabled, bootstrap token env var '{tokenEnv}' is set"));
        }
    }

    /// <summary>Fail in Production; outside it a Warn with the gate prefix stripped from the message.</summary>
    public static ProductionSecretFinding FailOrWarn(bool isProduction, string name, string gateMessage)
    {
        return isProduction
            ? new ProductionSecretFinding(name, ProductionSecretFinding.Severity.Fail, gateMessage)
            : new ProductionSecretFinding(
                name,
                ProductionSecretFinding.Severity.Warn,
                gateMessage.Replace("refusing to start the host in Production: ", string.Empty, StringComparison.Ordinal)
                    + " (allowed outside Production)");
    }
}

/// <summary>Pure rules shared by the audit checks (token env mapping, password strength).</summary>
file static class ProductionSecretAuditRules
{
    /// <summary>
    /// Maps a dictionary-provider scheme to the env-var name that holds the
    /// bootstrap token. Vault is deliberately absent — its gate reads the
    /// configurable <see cref="VaultSecretOptions.TokenEnvRef"/>, not a
    /// hard-coded name. Returns null for providers that do not require a
    /// token (env, file, null).
    /// </summary>
    public static string? ProviderTokenEnvName(string scheme)
    {
        return scheme switch
        {
            "consul" => "COMUKI_CONSUL_TOKEN",
            _ => null,
        };
    }

    /// <summary>
    /// True when the password is shorter than the configured minimum, or
    /// lacks at least one digit and at least one non-alphanumeric
    /// character. ASCII-only: a deployment gate, not a strength meter.
    /// </summary>
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
