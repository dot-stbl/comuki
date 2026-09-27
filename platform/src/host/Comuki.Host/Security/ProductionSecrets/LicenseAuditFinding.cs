using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing.Status;

namespace Comuki.Host.Security.ProductionSecrets;

/// <summary>
/// The <c>license.validity</c> row of the production-secret audit (issue
/// #164 E3, add-editions-and-licensing WS3.4): translates
/// <see cref="IEdition.Status"/> into a single
/// <see cref="ProductionSecretFinding"/> so <c>comuki doctor</c> and
/// the production <see cref="ProductionSecretValidator"/> see the
/// license through the same check-list surface as every other
/// secret. The lifetime rule is split between boot and runtime on
/// purpose — <c>Expired</c> at boot refuses to start, <c>Expired</c>
/// after a graceful degrade (the host was started under a still-valid
/// license that has since expired past grace) keeps the gate open for
/// reads / exports of data the customer already owns.
/// <list type="bullet">
///   <item><see cref="LicenseStatus.Valid"/> → <c>Ok</c> — license is in
///     its validity window; the row carries the current tier code so an
///     operator scanning the doctor output knows which tier is active.</item>
///   <item><see cref="LicenseStatus.Grace"/> → <c>Warn</c> — past
///     expiry but inside grace; reads continue, writes are still
///     permitted because the gate has not flipped to read-only yet
///     (see <c>EditionGate</c>). The Warn text tells the
///     operator the gate will degrade writes once grace ends.</item>
///   <item><see cref="LicenseStatus.Expired"/> → <c>Fail</c> — past
///     expiry + grace. In Production this throws at boot via
///     <see cref="ProductionSecretValidator"/>; if the host was
///     running and the license expired in place, the gate flips to
///     read-only and this row becomes the visible record of why.</item>
///   <item><see cref="LicenseStatus.Absent"/> → <c>Ok</c> — Community
///     edition, no license configured. Not a fault: the product ships
///     this way out of the box.</item>
///   <item><see cref="LicenseStatus.Unspecified"/> →
///     <c>Fail</c> — classification invariant violation. Never a legal
///     state; observing it means a missing branch in
///     <see cref="LicenseEvaluator"/>. Same boot-refusal contract as
///     <c>Expired</c> because the host cannot claim a tier it cannot
///     prove.</item>
/// </list>
/// </summary>
public static class LicenseAuditFinding
{
    /// <summary>One <c>license.validity</c> row of the audit, derived from the live <see cref="IEdition"/>.</summary>
    /// <param name="edition">The live edition port. When the host composition did not register an edition (e.g. a
    /// minimal provider-only build), the caller passes <c>null</c> and the row reads as Community.</param>
    /// <returns>One <see cref="ProductionSecretFinding"/> carrying the current license posture.</returns>
    public static ProductionSecretFinding Collect(IEdition? edition)
    {
        if (edition is null)
        {
            return new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Ok,
                "community edition (editions not composed)");
        }

        var status = edition.Status;
        var tierCode = edition.Current.Code;

        return status switch
        {
            _ when status == LicenseStatus.Valid => new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Ok,
                $"license valid (tier={tierCode})"),
            _ when status == LicenseStatus.Grace => new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Warn,
                $"license past expiry but inside the grace period (tier={tierCode}) - renew before enforcement degrades writes"),
            _ when status == LicenseStatus.Expired => new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Fail,
                "refusing to start the host in Production: the license expired past its grace period - renew or replace the license file"),
            _ when status == LicenseStatus.Absent => new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Ok,
                "community edition (no license configured)"),
            _ => new ProductionSecretFinding(
                "license.validity",
                ProductionSecretFinding.Severity.Fail,
                "license status unspecified - classification invariant violation"),
        };
    }
}
