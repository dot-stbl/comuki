using Comuki.Shared.Editions.Edition;
using Microsoft.AspNetCore.Http;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// Builds the <see cref="EditionDenial"/> shapes the gate emits:
/// feature-unavailable denials (regular + read-only-degrade-on-expiry)
/// and limit-exceeded denials. Owns the stable problem-detail
/// extension codes as constants — the gate never hand-builds
/// ProblemDetails so the wire shape stays canonical (RFC 9457,
/// dot.case <c>code</c>, typed extensions).
/// </summary>
internal static class EditionDenialBuilder
{
    /// <summary>The stable problem-detail extension code for feature denials — covers BOTH the ordinary "edition does not cover this feature" branch AND the read-only-degrade write refusal (same code, same status; the <c>licenseStatus</c> extension discriminates them on the client).</summary>
    public const string FeatureUnavailableCode = "edition.feature_unavailable";

    /// <summary>The stable problem-detail extension code for limit denials.</summary>
    public const string LimitExceededCode = "edition.limit_exceeded";

    /// <summary>The value carried in the <c>licenseStatus</c> extension when a paid-gated write is refused past grace — discriminates the degrade-write denial from an ordinary feature-unavailable on the client.</summary>
    public const string DegradedLicenseStatus = "expired";

    /// <summary>
    /// Builds a 403 <see cref="EditionDenial"/> for a feature demand the
    /// current edition does not cover.
    /// </summary>
    /// <param name="featureKey">The well-formed feature key that was demanded.</param>
    /// <param name="minimumTier">The minimum tier code (e.g. <c>"team"</c>) required to unlock the feature, or null when the feature is unknown.</param>
    /// <returns>The denial with status 403 and a typed ProblemDetails body.</returns>
    public static EditionDenial ForFeature(string featureKey, string? minimumTier)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = FeatureUnavailableCode, ["feature"] = featureKey };
        if (minimumTier is not null)
        {
            extensions["minimumTier"] = minimumTier;
        }

        var typed = TypedResults.Problem(
            title: "Feature unavailable",
            detail: $"feature '{featureKey}' is not available on the current edition",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: extensions);

        return new EditionDenial(StatusCodes.Status403Forbidden, typed.ProblemDetails);
    }

    /// <summary>
    /// Builds a 403 <see cref="EditionDenial"/> for a paid-gated WRITE
    /// request that arrived past the license's grace window
    /// (<see cref="IEdition.IsDegraded"/>). The denial reuses
    /// <see cref="FeatureUnavailableCode"/> deliberately — clients branch
    /// on a stable <c>code</c>, not on a separate "expired" code —
    /// and carries <c>licenseStatus: "expired"</c> as a typed
    /// extension so the dashboard / CLI can distinguish the
    /// degrade-write shape from an ordinary "edition does not cover
    /// this feature" denial without parsing free text. Reads (GET/HEAD)
    /// for the same feature are passed by the gate upstream — see
    /// <see cref="EditionGate.EvaluateFeature"/>'s degrade branch.
    /// </summary>
    /// <param name="featureKey">The well-formed feature key that was demanded on a write path.</param>
    /// <returns>The denial with status 403, the shared <see cref="FeatureUnavailableCode"/>, and the <c>licenseStatus</c> extension set to <see cref="DegradedLicenseStatus"/>.</returns>
    public static EditionDenial ForDegradedWrite(string featureKey)
    {
        var typed = TypedResults.Problem(
            title: "Feature unavailable",
            detail: $"license expired past its grace period: feature '{featureKey}' is read-only until the license is renewed",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = FeatureUnavailableCode,
                ["feature"] = featureKey,
                ["licenseStatus"] = DegradedLicenseStatus,
            });

        return new EditionDenial(StatusCodes.Status403Forbidden, typed.ProblemDetails);
    }

    /// <summary>
    /// Builds a 403 <see cref="EditionDenial"/> for a count-quota
    /// limit the current edition has exhausted.
    /// </summary>
    /// <param name="limitKey">The well-formed limit key that was demanded.</param>
    /// <param name="cap">The tier's effective cap.</param>
    /// <param name="current">The current usage at the moment the gate denied.</param>
    /// <returns>The denial with status 403 and a typed ProblemDetails body.</returns>
    public static EditionDenial ForLimit(string limitKey, int cap, int current)
    {
        var typed = TypedResults.Problem(
            title: "Limit exceeded",
            detail: $"limit '{limitKey}' is exhausted ({current}/{cap})",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = LimitExceededCode,
                ["limit"] = limitKey,
                ["cap"] = cap,
                ["current"] = current,
            });

        return new EditionDenial(StatusCodes.Status403Forbidden, typed.ProblemDetails);
    }
}
