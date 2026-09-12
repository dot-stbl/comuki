namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// Startup validator that refuses to boot the host in <c>Production</c>
/// when an obvious default-credentials secret is still present: MinIO
/// keys, bootstrap-admin password, API-key pepper, worker-token pepper,
/// and — since issue #52 — a remote secrets provider that's been enabled
/// but whose bootstrap token env var is unset. The migrator has its own
/// database-password gate
/// (<c>ConnectionStringSource.RejectBlankPasswordInProduction</c>); this
/// one extends the same discipline to the remaining dev-default values
/// (issue #10 T11.4 + security audit A02-1 — production deployments
/// without the env vars were silently bootting with public-domain HMAC
/// peppers, allowing anyone to forge keys or tokens). Production-only
/// length / character-class check on the bootstrap-admin password (Q29)
/// keeps operators from deploying with <c>password123</c>.
/// <para>
/// The per-secret logic lives in <see cref="ProductionSecretAudit"/> (one
/// finding per check); this gate turns the Fail findings into the boot
/// refusal. <c>comuki doctor</c> (issue #56) prints the same findings as
/// an ok/warn/fail checklist.
/// </para>
/// </summary>
public static class ProductionSecretValidator
{
    /// <summary>Minimum password length for the bootstrap admin in <c>Production</c>.</summary>
    public const int BootstrapPasswordMinLength = 12;

    /// <summary>
    /// Inspects the bound options for production-unsafe defaults and throws
    /// in <c>Production</c> when any audited secret is still on its
    /// committed dev value — i.e. someone forgot to override the env var
    /// (or config.toml equivalent).
    /// </summary>
    /// <param name="services">The host's service collection (used to read <see cref="IHostEnvironment"/> + the bound options).</param>
    /// <exception cref="InvalidOperationException">A production-unsafe secret is still on its committed default.</exception>
    public static void Validate(IServiceProvider services)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsProduction())
        {
            return;
        }

        foreach (var finding in ProductionSecretAudit.Collect(services))
        {
            if (finding.SeverityLevel == ProductionSecretFinding.Severity.Fail)
            {
                throw new InvalidOperationException(finding.Detail);
            }
        }
    }
}
