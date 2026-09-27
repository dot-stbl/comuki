using Comuki.Shared.Editions.Tiers;

namespace Comuki.Shared.Editions.Licensing.Status;

/// <summary>
/// Pure classifier: given an optional <see cref="LicenseKey"/> and a
/// wall-clock instant, classifies the license into one of
/// <see cref="LicenseStatus.Valid"/> / <see cref="LicenseStatus.Grace"/>
/// / <see cref="LicenseStatus.Expired"/> (or
/// <see cref="LicenseStatus.Absent"/> when no license applies), and
/// pins the effective edition tier (always <see cref="EditionTier.Community"/>
/// in the absent case).
/// <para>
/// The verifier (<see cref="ILicenseProvider"/>) and this evaluator are
/// deliberately separate. The verifier proves the token is authentic
/// and well-formed; this one decides what its fields mean at the
/// current instant. A consumer that wants a different decision (e.g.
/// "ignore expiry during an upgrade window") supplies its own evaluator,
/// not a patched verifier.
/// </para>
/// </summary>
public static class LicenseEvaluator
{
    /// <summary>The classification outcome: a <see cref="LicenseStatus"/> and the effective <see cref="EditionTier"/>.</summary>
    /// <param name="Status">Where the license currently sits in its lifecycle.</param>
    /// <param name="Current">The effective tier right now: <see cref="EditionTier.Community"/> when <see cref="LicenseStatus.Absent"/>, else the licensed tier.</param>
    public readonly record struct Result(LicenseStatus Status, EditionTier Current);

    /// <summary>Classifies <paramref name="license"/> at <paramref name="now"/>.</summary>
    /// <param name="license">The verified license, or <c>null</c> when none was provided.</param>
    /// <param name="now">The current instant (typically <see cref="TimeProvider.GetUtcNow"/>).</param>
    /// <param name="gracePeriod">How long after <see cref="LicenseKey.Expiry"/> the license still reports <see cref="LicenseStatus.Grace"/>.</param>
    /// <returns>The classification and effective tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gracePeriod"/> is negative.</exception>
    public static Result Classify(LicenseKey? license, DateTimeOffset now, TimeSpan gracePeriod)
    {
        return gracePeriod < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(gracePeriod), gracePeriod, "Grace period cannot be negative.")
            : license switch
            {
                null => new Result(LicenseStatus.Absent, EditionTier.Community),
                { NotBefore: { } notBefore } when now < notBefore
                    => new Result(LicenseStatus.Absent, EditionTier.Community),
                { Expiry: var expiry } k when now <= expiry
                    => new Result(LicenseStatus.Valid, k.Tier),
                { Expiry: var expiry } k when now <= expiry + gracePeriod
                    => new Result(LicenseStatus.Grace, k.Tier),
                var k
                    => new Result(LicenseStatus.Expired, k.Tier),
            };
    }
}
