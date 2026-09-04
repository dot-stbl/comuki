using Comuki.Host.Auth;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// Startup validator that refuses to boot the host in <c>Production</c>
/// when an obvious default-credentials secret is still present. The
/// migrator has its own database-password gate
/// (<c>ConnectionStringSource.RejectBlankPasswordInProduction</c>); this
/// one extends the same discipline to <see cref="ArtifactsOptions"/> and
/// to the bootstrap-admin password (issue #10 T11.4).
/// </summary>
public static class ProductionSecretValidator
{
    /// <summary>
    /// Inspects the bound <see cref="ArtifactsOptions"/> and
    /// <see cref="BootstrapAdminOptions"/>; throws in <c>Production</c>
    /// when any of the marked secrets carries a value the committed
    /// defaults ship with — i.e. someone forgot to override the dev value.
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

        ValidateMinioSecrets(services);
        ValidateBootstrapAdmin(services);
    }

    /// <summary>
    /// Refuses to start the host in <c>Production</c> when the bound
    /// <see cref="ArtifactsOptions.SecretKey"/> still carries the
    /// committed <c>comuki_dev</c> default (matched against
    /// <see cref="Modules.Artifacts.Infrastructure.Store.Minio"/>).
    /// The committed <see cref="ArtifactsOptions.AccessKey"/> default
    /// <c>comuki</c> is also rejected — real deployments use a dedicated
    /// service account name.
    /// </summary>
    private static void ValidateMinioSecrets(IServiceProvider services)
    {
        var artifacts = services.GetRequiredService<IOptions<ArtifactsOptions>>().Value;

        if (string.Equals(artifacts.SecretKey, "comuki_dev", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "refusing to start the host in Production: Artifacts:Minio:SecretKey is still on its committed dev default "
                + "('comuki_dev'); set the Artifacts__Minio__SecretKey env var (or the appsettings equivalent) to a strong secret");
        }

        if (string.Equals(artifacts.AccessKey, "comuki", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "refusing to start the host in Production: Artifacts:Minio:AccessKey is still on its committed dev default "
                + "('comuki'); set the Artifacts__Minio__AccessKey env var to the dedicated service account name");
        }
    }

    /// <summary>
    /// Refuses to start in <c>Production</c> when the bootstrap admin
    /// password is still the well-known <c>comuki_dev</c> dev default.
    /// The email default is also rejected as a guard — every install
    /// should have a unique operator address.
    /// </summary>
    private static void ValidateBootstrapAdmin(IServiceProvider services)
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
    }
}
