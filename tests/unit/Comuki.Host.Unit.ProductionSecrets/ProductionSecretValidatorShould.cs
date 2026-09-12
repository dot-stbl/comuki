using Comuki.Engine.Compute.Options;
using Comuki.Host.Auth;
using Comuki.Host.Security.ProductionSecrets;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.ProductionSecrets;

/// <summary>
/// Unit tests for <see cref="ProductionSecretValidator"/>: refuses to start
/// the host in <c>Production</c> when any committed dev-default secret
/// (MinIO keys, bootstrap admin password, API-key pepper, worker-token
/// pepper, enabled remote secrets provider without its bootstrap token)
/// is still on its shipped value, and returns silently in
/// non-production environments. Q29 adds the length + character-class
/// check on the bootstrap admin password; security audit A02-1 adds the
/// pepper checks; issue #52 adds the secrets-provider check.
/// </summary>
public sealed class ProductionSecretValidatorShould : IDisposable
{
    public ProductionSecretValidatorShould()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, null);
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, null);
        Environment.SetEnvironmentVariable(ApiKeyOptions.PepperEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(WorkerTokenOptions.PepperEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef, null);
        Environment.SetEnvironmentVariable("COMUKI_CONSUL_TOKEN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, null);
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, null);
        Environment.SetEnvironmentVariable(ApiKeyOptions.PepperEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(WorkerTokenOptions.PepperEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, null);
        Environment.SetEnvironmentVariable(ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef, null);
        Environment.SetEnvironmentVariable("COMUKI_CONSUL_TOKEN", null);
    }

    [Fact(DisplayName = "Given Production + bootstrap password too short, when Validate is called, then it throws with a length hint")]
    public void ThrowWhenProductionBootstrapPasswordIsTooShort()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "Short1!");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("too weak");
        exception.Message.ShouldContain("12");
    }

    [Fact(DisplayName = "Given Production + bootstrap password missing a digit, when Validate is called, then it throws with a digit hint")]
    public void ThrowWhenProductionBootstrapPasswordHasNoDigit()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "longpasswordnodigit!");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("too weak");
    }

    [Fact(DisplayName = "Given Production + bootstrap password missing a non-alphanumeric, when Validate is called, then it throws with a symbol hint")]
    public void ThrowWhenProductionBootstrapPasswordHasNoSymbol()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "longpassword1234");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("too weak");
    }

    [Fact(DisplayName = "Given Production + bootstrap password meeting every rule, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenProductionBootstrapPasswordIsStrong()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "Strong-Production-Pass-2026!");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Development env and dev defaults, when Validate is called, then it returns silently")]
    public void ReturnSilentlyInDevelopment()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Development,
            ProductionSecretsTestArtifacts.DevDefaults());

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + Minio SecretKey still default, when Validate is called, then it throws")]
    public void ThrowWhenProductionMinioSecretKeyStillDefault()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.WithSecretKey("comuki_dev"));

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("Artifacts:Minio:SecretKey");
        exception.Message.ShouldContain("comuki_dev");
    }

    [Fact(DisplayName = "Given Production + Minio AccessKey still default, when Validate is called, then it throws")]
    public void ThrowWhenProductionMinioAccessKeyStillDefault()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.WithAccessKey("comuki"));

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("Artifacts:Minio:AccessKey");
    }

    [Fact(DisplayName = "Given Production + bootstrap admin password default, when Validate is called, then it throws")]
    public void ThrowWhenProductionBootstrapAdminPasswordStillDefault()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "comuki_dev");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(BootstrapAdminOptions.PasswordEnvVariable);
        exception.Message.ShouldContain("comuki_dev");
    }

    [Fact(DisplayName = "Given Production + bootstrap admin not configured, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenBootstrapAdminNotConfigured()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + all secrets overridden via env/config, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenAllSecretsOverridden()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "StrongProdP@ss-2026");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + API-key pepper on its dev default, when Validate is called, then it throws with the pepper env-var name")]
    public void ThrowWhenProductionApiKeyPepperStillDefault()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            apiKeyPepper: ProductionSecretsTestArtifacts.DevDefaultApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper);

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(ApiKeyOptions.PepperEnvironmentVariable);
        exception.Message.ShouldContain(ProductionSecretsTestArtifacts.DevDefaultApiKeyPepper);
    }

    [Fact(DisplayName = "Given Production + API-key pepper overridden, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenProductionApiKeyPepperIsOverridden()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "StrongProdP@ss-2026");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper);

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + worker-token pepper on its dev default, when Validate is called, then it throws with the pepper env-var name")]
    public void ThrowWhenProductionWorkerTokenPepperStillDefault()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.DevDefaultWorkerTokenPepper);

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(WorkerTokenOptions.PepperEnvironmentVariable);
        exception.Message.ShouldContain(ProductionSecretsTestArtifacts.DevDefaultWorkerTokenPepper);
    }

    [Fact(DisplayName = "Given Production + worker-token pepper overridden, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenProductionWorkerTokenPepperIsOverridden()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, "ops@example.com");
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, "StrongProdP@ss-2026");

        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper);

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + only the API-key pepper overridden (worker-token still default), when Validate is called, then it throws on the worker-token pepper")]
    public void ThrowWhenProductionWorkerTokenPepperStillDefaultEvenWithApiKeyOverridden()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.DevDefaultWorkerTokenPepper);

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(WorkerTokenOptions.PepperEnvironmentVariable);
        exception.Message.ShouldNotContain(ApiKeyOptions.PepperEnvironmentVariable);
    }

    [Fact(DisplayName = "Given Production + vault provider enabled but the default token env var unset, when Validate is called, then it throws naming the env var")]
    public void ThrowWhenProductionVaultProviderEnabledWithoutToken()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            vault: new VaultSecretOptions { Enabled = true });

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(VaultSecretOptions.DefaultTokenEnvVariable);
        exception.Message.ShouldContain(VaultSecretOptions.SectionName);
    }

    [Fact(DisplayName = "Given Production + vault provider enabled with the default token env var set, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenProductionVaultProviderEnabledAndTokenSet()
    {
        Environment.SetEnvironmentVariable(VaultSecretOptions.DefaultTokenEnvVariable, "real-vault-token-2026");
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            vault: new VaultSecretOptions { Enabled = true });

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + vault provider enabled with a custom TokenEnvRef that is unset, when Validate is called, then it throws naming the custom env var, not the default")]
    public void ThrowWhenProductionVaultCustomTokenEnvRefUnset()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            vault: new VaultSecretOptions
            {
                Enabled = true,
                TokenEnvRef = ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef,
            });

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain(ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef);
        exception.Message.ShouldNotContain(VaultSecretOptions.DefaultTokenEnvVariable);
    }

    [Fact(DisplayName = "Given Production + vault provider enabled with a custom TokenEnvRef that is set, when Validate is called, then it returns silently")]
    public void ReturnSilentlyWhenProductionVaultCustomTokenEnvRefSet()
    {
        Environment.SetEnvironmentVariable(ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef, "custom-vault-token-2026");
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            vault: new VaultSecretOptions
            {
                Enabled = true,
                TokenEnvRef = ProductionSecretsTestArtifacts.CustomVaultTokenEnvRef,
            });

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }

    [Fact(DisplayName = "Given Production + consul provider enabled but COMUKI_CONSUL_TOKEN unset, when Validate is called, then it throws naming the env var")]
    public void ThrowWhenProductionConsulProviderEnabledWithoutToken()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            secrets: ProductionSecretsTestArtifacts.ConsulEnabledButTokenUnset());

        var exception = Should.Throw<InvalidOperationException>(
            () => ProductionSecretValidator.Validate(services.BuildServiceProvider()));

        exception.Message.ShouldContain("COMUKI_CONSUL_TOKEN");
    }

    [Fact(DisplayName = "Given Production + no remote providers enabled, when Validate is called, then it returns silently (env / file providers are token-less)")]
    public void ReturnSilentlyWhenNoRemoteProvidersEnabled()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden(),
            secrets: ProductionSecretsTestArtifacts.NoRemoteProviders());

        Should.NotThrow(() => ProductionSecretValidator.Validate(services.BuildServiceProvider()));
    }
    [Fact(DisplayName = "Given Development + dev defaults, when the audit is collected, then the findings warn instead of failing")]
    public void CollectWarnsOnDevDefaultsOutsideProduction()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Development,
            ProductionSecretsTestArtifacts.DevDefaults(),
            ProductionSecretsTestArtifacts.DevDefaultApiKeyPepper,
            ProductionSecretsTestArtifacts.DevDefaultWorkerTokenPepper);

        var findings = ProductionSecretAudit.Collect(services.BuildServiceProvider());

        var apiKey = findings.Single(static finding => finding.Name == "apikey-pepper");
        apiKey.SeverityLevel.ShouldBe(ProductionSecretFinding.Severity.Warn);
        apiKey.Detail.ShouldContain("allowed outside Production");
        findings.ShouldContain(static finding => finding.Name == "minio.secretkey" && finding.SeverityLevel == ProductionSecretFinding.Severity.Warn);
    }

    [Fact(DisplayName = "Given Production + dev defaults, when the audit is collected, then the findings fail with the startup-gate messages")]
    public void CollectFailsOnDevDefaultsInProduction()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.DevDefaults(),
            ProductionSecretsTestArtifacts.DevDefaultApiKeyPepper,
            ProductionSecretsTestArtifacts.DevDefaultWorkerTokenPepper);

        var findings = ProductionSecretAudit.Collect(services.BuildServiceProvider());

        findings.ShouldContain(static finding => finding.Name == "apikey-pepper" && finding.SeverityLevel == ProductionSecretFinding.Severity.Fail);
        findings.ShouldContain(static finding => finding.Name == "minio.secretkey" && finding.SeverityLevel == ProductionSecretFinding.Severity.Fail);
        findings.Single(static finding => finding.Name == "apikey-pepper").Detail.ShouldContain("refusing to start the host in Production");
    }

    [Fact(DisplayName = "Given every secret overridden, when the audit is collected, then all findings are ok")]
    public void CollectReportsOkWhenEverythingOverridden()
    {
        var services = ProductionSecretsTestServices.BuildServices(
            Environments.Production,
            ProductionSecretsTestArtifacts.AllOverridden());

        var findings = ProductionSecretAudit.Collect(services.BuildServiceProvider());

        findings.ShouldAllBe(static finding => finding.SeverityLevel == ProductionSecretFinding.Severity.Ok);
    }
}

file static class ProductionSecretsTestServices
{
    public static IServiceCollection BuildServices(string environmentName, ArtifactsOptions artifacts)
    {
        return BuildServices(
            environmentName,
            artifacts,
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper);
    }

    public static IServiceCollection BuildServices(string environmentName, ArtifactsOptions artifacts, SecretsOptions secrets)
    {
        return BuildServices(
            environmentName,
            artifacts,
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper,
            secrets: secrets);
    }

    public static IServiceCollection BuildServices(string environmentName, ArtifactsOptions artifacts, VaultSecretOptions vault)
    {
        return BuildServices(
            environmentName,
            artifacts,
            apiKeyPepper: ProductionSecretsTestArtifacts.OverriddenApiKeyPepper,
            workerTokenPepper: ProductionSecretsTestArtifacts.OverriddenWorkerTokenPepper,
            vault: vault);
    }

    public static IServiceCollection BuildServices(
        string environmentName,
        ArtifactsOptions artifacts,
        string apiKeyPepper,
        string workerTokenPepper,
        SecretsOptions? secrets = null,
        VaultSecretOptions? vault = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new ProductionSecretsTestEnvironment(environmentName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Options.Create(artifacts));
        services.AddSingleton(Options.Create(new ApiKeyOptions { Pepper = apiKeyPepper }));
        services.AddSingleton(Options.Create(new WorkerTokenOptions { Pepper = workerTokenPepper }));
        services.AddSingleton(Options.Create(secrets ?? ProductionSecretsTestArtifacts.NoRemoteProviders()));
        services.AddSingleton(Options.Create(vault ?? new VaultSecretOptions()));
        return services;
    }
}

file static class ProductionSecretsTestArtifacts
{
    public const string Endpoint = "minio:9000";
    public const string Bucket = "comuki-artifacts";

    public const string DevDefaultApiKeyPepper = "comuki-dev-only-apikey-pepper-override-in-production";
    public const string DevDefaultWorkerTokenPepper = "comuki-dev-only-pepper-override-in-production";

    public const string OverriddenApiKeyPepper = "rotated-strong-apikey-pepper-2026";
    public const string OverriddenWorkerTokenPepper = "rotated-strong-worker-token-pepper-2026";

    /// <summary>Custom bootstrap-token env-var name for the TokenEnvRef override cases (env-var NAME — values stay in env only).</summary>
    public const string CustomVaultTokenEnvRef = "MY_VAULT_TOKEN";

    public static ArtifactsOptions DevDefaults()
    {
        return new()
        {
            Endpoint = Endpoint,
            AccessKey = "comuki",
            SecretKey = "comuki_dev",
            Bucket = Bucket,
        };
    }

    public static ArtifactsOptions WithSecretKey(string secretKey)
    {
        return new()
        {
            Endpoint = Endpoint,
            AccessKey = "comuki",
            SecretKey = secretKey,
            Bucket = Bucket,
        };
    }

    public static ArtifactsOptions WithAccessKey(string accessKey)
    {
        return new()
        {
            Endpoint = Endpoint,
            AccessKey = accessKey,
            SecretKey = "rotated-strong-secret",
            Bucket = Bucket,
        };
    }

    public static ArtifactsOptions AllOverridden()
    {
        return new()
        {
            Endpoint = "minio.internal",
            AccessKey = "service-account",
            SecretKey = "rotated-strong-secret",
            Bucket = Bucket,
        };
    }

    public static SecretsOptions NoRemoteProviders()
    {
        return new SecretsOptions
        {
            Providers = new Dictionary<string, FileSecretOptions>(StringComparer.OrdinalIgnoreCase),
        };
    }

    public static SecretsOptions ConsulEnabledButTokenUnset()
    {
        return new SecretsOptions
        {
            Providers = new Dictionary<string, FileSecretOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["consul"] = new FileSecretOptions { Enabled = true },
            },
        };
    }
}

file sealed class ProductionSecretsTestEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Comuki.Unit.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
