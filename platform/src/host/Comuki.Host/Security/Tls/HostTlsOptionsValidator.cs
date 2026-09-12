using Microsoft.Extensions.Options;

namespace Comuki.Host.Security.Tls;

/// <summary>
/// Cross-field validation for <see cref="HostTlsOptions"/>: the PEM
/// certificate / key paths must arrive as a pair, and the development
/// certificate opt-in must not be combined with mounted PEM files.
/// Range/required checks live on the options class as data annotations;
/// these rules bind more than one field, so they run here at
/// <c>ValidateOnStart</c> time.
/// </summary>
public sealed class HostTlsOptionsValidator : IValidateOptions<HostTlsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, HostTlsOptions options)
    {
        var hasCertificate = !string.IsNullOrWhiteSpace(options.CertificatePath);
        var hasKey = !string.IsNullOrWhiteSpace(options.CertificateKeyPath);
        var failures = new List<string>();

        if (hasCertificate != hasKey)
        {
            failures.Add(
                "Host:Tls CertificatePath and CertificateKeyPath must be set together — a PEM certificate always needs its private key file.");
        }

        if (options.UseDevCertificate == true && hasCertificate)
        {
            failures.Add(
                "Host:Tls UseDevCertificate cannot be combined with CertificatePath/CertificateKeyPath — mount the PEM pair or rely on the development certificate, not both.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
