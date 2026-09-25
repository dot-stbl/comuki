using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Registry;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// The one edition-gating decision shared by the MVC resource filter and
/// the minimal-API middleware: registry lookup plus edition evaluation,
/// producing the canonical 403 problem or null when the demand is
/// satisfied. Mirrors <c>PermissionGate.EvaluateAsync</c>'s shared-decision
/// shape (Identity module).
/// </summary>
/// <remarks>
/// The limit branch (<see cref="EvaluateLimitAsync"/>) is advisory only:
/// it serves the request-time filter short-circuit and may race with
/// concurrent writers (a read-then-write TOCTOU window). The
/// authoritative enforcement happens at the write site
/// (<c>CreateProjectHandler</c>, etc.) inside the same database
/// transaction as the insert, via a <c>pg_advisory_xact_lock</c> that
/// serialises competing writers on the limit-key. The filter is kept as
/// a fast-fail so a caller already over the cap never burns a database
/// transaction on the denied write — but the handler is the source of
/// truth and a race there loses by a 403, never by a successful insert.
/// </remarks>
internal static class EditionGate
{
    /// <summary>Evaluates the feature demand against the edition; null = allowed.</summary>
    /// <param name="registry">Catalog registry.</param>
    /// <param name="edition">Runtime read-side of the current license.</param>
    /// <param name="featureKey">Demanded key.</param>
    public static EditionDenial? EvaluateFeature(
        IEditionCapabilityRegistry registry,
        IEdition edition,
        string featureKey)
    {
        // Unknown / malformed key => fail closed (deny). A real gap here is
        // caught at build time by the (future, out-of-scope) architecture
        // test that scans every [RequiresFeature]/[EnforceLimit] call site
        // against the registry — this runtime path never trusts an
        // unrecognised key.
        var feature = FeatureKey.IsWellFormed(featureKey)
                      && registry.TryGetFeature(FeatureKey.Parse(featureKey), out var resolved)
                      && resolved is not null
            ? resolved
            : null;

        return feature is null
            ? EditionDenialBuilder.ForFeature(featureKey, minimumTier: null)
            : edition.Has(feature)
                ? null
                : EditionDenialBuilder.ForFeature(feature.Key.Value, TierCodes.RankToCode(feature.MinimumRank));
    }

    /// <summary>Evaluates the limit demand against the edition; null = allowed.</summary>
    /// <remarks>
    /// Advisory only — the authoritative cap check happens transactionally
    /// in the handler. See class remarks.
    /// </remarks>
    /// <param name="registry">Catalog registry.</param>
    /// <param name="edition">Runtime read-side of the current license.</param>
    /// <param name="limitUsageProviders">Concrete usage providers keyed by <see cref="ILimitUsageProvider.LimitKey"/>.</param>
    /// <param name="limitKey">Demanded key.</param>
    /// <param name="cancellationToken">Cancellation tied to the gating request.</param>
    public static async Task<EditionDenial?> EvaluateLimitAsync(
        IEditionCapabilityRegistry registry,
        IEdition edition,
        IEnumerable<ILimitUsageProvider> limitUsageProviders,
        string limitKey,
        CancellationToken cancellationToken)
    {
        if (!LimitKey.IsWellFormed(limitKey)
            || !registry.TryGetLimit(LimitKey.Parse(limitKey), out var limit)
            || limit is null)
        {
            return EditionDenialBuilder.ForLimit(limitKey, cap: 0, current: 0);
        }

        var cap = edition.Limit(limit);
        var provider = limitUsageProviders.FirstOrDefault(
            candidate => string.Equals(candidate.LimitKey.Value, limit.Key.Value, StringComparison.Ordinal));

        // A declared limit with no registered usage provider is a wiring
        // gap, not a free pass: fail closed so a missing registration is
        // loudly visible (a 403 in every environment) rather than silently
        // unenforced.
        if (provider is null)
        {
            return EditionDenialBuilder.ForLimit(limit.Key.Value, cap, current: cap);
        }

        var current = await provider.CurrentAsync(cancellationToken);
        return current >= cap
            ? EditionDenialBuilder.ForLimit(limit.Key.Value, cap, current)
            : null;
    }
}
