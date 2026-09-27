namespace Comuki.Shared.Editions.Licensing;

/// <summary>
/// Parses and cryptographically verifies a license token. Throws
/// <see cref="LicenseInvalidException"/> for a bad/mismatched
/// signature, malformed base64/JSON, or an unrecognised edition
/// code. Deliberately does NOT check <see cref="LicenseKey.Expiry"/>
/// or <see cref="LicenseKey.NotBefore"/> against the clock — expiry
/// and grace-period classification is
/// <see cref="Status.LicenseEvaluator"/>'s job (a later consumer
/// decides what "expired" means at what time; this method only proves
/// the token is authentic and well-formed).
/// </summary>
public interface ILicenseProvider
{
    /// <summary>Verifies <paramref name="token"/> and returns its trusted <see cref="LicenseKey"/>.</summary>
    /// <exception cref="LicenseInvalidException"><paramref name="token"/> is shaped wrong, signature-mismatched, malformed, or names an unknown edition.</exception>
    public LicenseKey Verify(string token);
}
