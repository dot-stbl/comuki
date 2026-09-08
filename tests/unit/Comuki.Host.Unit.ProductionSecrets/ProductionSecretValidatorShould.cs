using Comuki.Host.Auth;
using Comuki.Host.Security.ProductionSecrets;
using Comuki.Modules.Artifacts.Infrastructure.Store;
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
/// the host in <c>Production</c> when the bound <see cref="ArtifactsOptions"/>
/// MinIO keys or the bootstrap admin password are still on their committed
/// dev defaults, and returns silently in non-production environments. Q29
/// adds the length + character-class check on the bootstrap admin password.
/// </summary>
public sealed class ProductionSecretValidatorShould : IDisposable
{
    public ProductionSecretValidatorShould()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, null);
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.EmailEnvVariable, null);
        Environment.SetEnvironmentVariable(BootstrapAdminOptions.PasswordEnvVariable, null);
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
}

file static class ProductionSecretsTestServices
{
    public static IServiceCollection BuildServices(string environmentName, ArtifactsOptions artifacts)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new ProductionSecretsTestEnvironment(environmentName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Options.Create(artifacts));
        return services;
    }
}

file static class ProductionSecretsTestArtifacts
{
    public const string Endpoint = "minio:9000";
    public const string Bucket = "comuki-artifacts";

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
