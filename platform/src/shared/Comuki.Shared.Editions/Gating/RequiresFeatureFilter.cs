using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Registry;
using Comuki.Shared.Editions.Tiers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// The action axis of the edition-gating model, enforced once per request
/// for every MVC action: reads <see cref="RequiresFeatureAttribute"/> and
/// <see cref="EnforceLimitAttribute"/> off the endpoint metadata, runs
/// them through <see cref="EditionGate"/>, and short-circuits with a 403
/// <c>problem+json</c> body on deny. Minimal-API endpoints are covered
/// by <see cref="RequiresFeatureMiddleware"/>; both share
/// <see cref="EditionGate"/> so the decision exists once.
/// </summary>
/// <param name="edition">The runtime read-side of the current license.</param>
/// <param name="registry">The in-memory catalog every gate call site reads from.</param>
/// <param name="limitUsageProviders">Concrete limit-usage providers, looked up by <see cref="ILimitUsageProvider.LimitKey"/>.</param>
/// <remarks>
/// A resource filter rather than an authorization filter deliberately:
/// this filter owns the whole check inline, and the resource stage wraps
/// model binding and result execution, so the decision cannot be
/// bypassed by an earlier short-circuit. Mirrors the Identity module's
/// <c>RequiresPermissionFilter</c> shape exactly (issue #164 §3a).
/// </remarks>
public sealed class RequiresFeatureFilter(
    IEdition edition,
    IEditionCapabilityRegistry registry,
    IEnumerable<ILimitUsageProvider> limitUsageProviders) : IAsyncResourceFilter
{
    /// <summary>The stable problem-detail extension code for feature denials.</summary>
    public const string FeatureUnavailableCode = "edition.feature_unavailable";

    /// <summary>The stable problem-detail extension code for limit denials.</summary>
    public const string LimitExceededCode = "edition.limit_exceeded";

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Last wins — endpoint metadata is ordered least to most specific
        // (controller attributes before action attributes), matching the
        // framework's own reader.
        var featureDemand = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiresFeatureAttribute>()
            .LastOrDefault();

        var limitDemand = context.ActionDescriptor.EndpointMetadata
            .OfType<EnforceLimitAttribute>()
            .LastOrDefault();

        if (featureDemand is null && limitDemand is null)
        {
            await next();
            return;
        }

        var cancellationToken = context.HttpContext.RequestAborted;

        // Feature demand evaluated first: a missing feature is the louder
        // failure mode and surfaces above the softer limit-exhausted
        // message when an endpoint carries both attributes.
        if (featureDemand is not null
            && EditionGate.EvaluateFeature(registry, edition, featureDemand.FeatureKey) is { } featureDenial)
        {
            context.Result = new ObjectResult(featureDenial.Problem)
            {
                StatusCode = featureDenial.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        if (limitDemand is not null
            && await EditionGate.EvaluateLimitAsync(registry, edition, limitUsageProviders, limitDemand.LimitKey, cancellationToken) is { } limitDenial)
        {
            context.Result = new ObjectResult(limitDenial.Problem)
            {
                StatusCode = limitDenial.StatusCode,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }
}

/// <summary>
/// The one edition-gating decision shared by the MVC resource filter and
/// the minimal-API middleware: registry lookup plus edition evaluation,
/// producing the canonical 403 problem or null when the demand is
/// satisfied. Mirrors <c>PermissionGate.EvaluateAsync</c>'s shared-decision
/// shape (Identity module).
/// </summary>
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
            ? FeatureDenial(featureKey, minimumTier: null)
            : edition.Has(feature)
                ? null
                : FeatureDenial(feature.Key.Value, MinimumTierCode(feature.MinimumRank));
    }

    /// <summary>Evaluates the limit demand against the edition; null = allowed.</summary>
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
            return LimitDenial(limitKey, cap: 0, current: 0);
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
            return LimitDenial(limit.Key.Value, cap, current: cap);
        }

        var current = await provider.CurrentAsync(cancellationToken);
        return current >= cap ? LimitDenial(limit.Key.Value, cap, current) : null;
    }

    /// <summary>
    /// Resolves a <see cref="Feature.MinimumRank"/> to its
    /// <see cref="EditionTier.Code"/>; null when no tier carries that rank.
    /// </summary>
    private static string? MinimumTierCode(int minimumRank)
    {
        foreach (var tier in EditionTiers.All)
        {
            if (tier.Rank == minimumRank)
            {
                return tier.Code;
            }
        }

        return null;
    }

    private static EditionDenial FeatureDenial(string featureKey, string? minimumTier)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = RequiresFeatureFilter.FeatureUnavailableCode, ["feature"] = featureKey };
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

    private static EditionDenial LimitDenial(string limitKey, int cap, int current)
    {
        var typed = TypedResults.Problem(
            title: "Limit exceeded",
            detail: $"limit '{limitKey}' is exhausted ({current}/{cap})",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = RequiresFeatureFilter.LimitExceededCode,
                ["limit"] = limitKey,
                ["cap"] = cap,
                ["current"] = current,
            });

        return new EditionDenial(StatusCodes.Status403Forbidden, typed.ProblemDetails);
    }
}

/// <summary>One deny decision: the status plus the ready ProblemDetails body.</summary>
/// <param name="StatusCode">403 — the only status edition-gating produces.</param>
/// <param name="Problem">ProblemDetails body to serialize.</param>
internal sealed record EditionDenial(int StatusCode, ProblemDetails Problem);
