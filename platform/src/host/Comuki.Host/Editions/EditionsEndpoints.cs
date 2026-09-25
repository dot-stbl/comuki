using System.Text.Json.Serialization;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Registry;

namespace Comuki.Host.Editions;

/// <summary>
/// Wire shape of <c>GET /api/v1/edition</c> — the dashboard and CLI's
/// read-only projection of the current license state. Anonymous by design
/// (the dashboard's first paint renders an upsell before sign-in), so the
/// endpoint is intentionally absent any permission / feature / role gates.
/// The response carries the live tier code and the current license
/// status (<c>valid</c> / <c>grace</c> / <c>expired</c> / <c>absent</c>);
/// <see cref="ExpiresAt"/> is omitted from the JSON entirely (not null)
/// when the license is absent, so a Community reader can branch on the
/// property's presence rather than its value.
/// </summary>
/// <param name="Tier">Lowercase tier code the license names, or <c>"community"</c> when absent.</param>
/// <param name="Status">One of <c>valid</c> / <c>grace</c> / <c>expired</c> / <c>absent</c>.</param>
/// <param name="Features">Every registered feature, with <c>available</c> reflecting the current edition.</param>
/// <param name="Limits">Every registered limit, with <c>cap</c> from the tier and <c>current</c> from the matching <c>ILimitUsageProvider</c>.</param>
/// <param name="Version">Lowercase assembly version (<c>ComukiBuildInformation.Version</c>) the host boots from.</param>
/// <param name="ExpiresAt">ISO-8601 UTC expiry from the verified license; omitted when <c>Status</c> is <c>absent</c>.</param>
public sealed record EditionView(
    string Tier,
    string Status,
    IReadOnlyList<FeatureAvailabilityView> Features,
    IReadOnlyList<LimitUsageView> Limits,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ExpiresAt);

/// <summary>
/// One feature's availability at the current edition. The view is honest
/// about every entry — including paid keys under Community, where
/// <c>available</c> is <c>false</c> — so the dashboard can render the
/// right disabled/locked affordance without a second call to the catalog.
/// </summary>
/// <param name="Key">The closed-vocabulary feature key (dot.case; the same string the gate attribute carries).</param>
/// <param name="Available">True when the current <see cref="IEdition.Has"/> covers this key.</param>
public sealed record FeatureAvailabilityView(string Key, bool Available);

/// <summary>
/// One limit's usage against its tier cap. The <c>current</c> value comes
/// from the matching <see cref="ILimitUsageProvider"/> registered in DI;
/// when no provider is registered the view reports <c>0</c> rather than
/// refusing to answer, because the gate is the source of truth for
/// enforcement and the view is read-only.
/// </summary>
/// <param name="Key">The closed-vocabulary limit key (dot.case; the same string the gate attribute carries).</param>
/// <param name="Current">Currently-observed usage (best-effort from the matching provider, or 0).</param>
/// <param name="Cap">Effective numeric cap at the current tier.</param>
public sealed record LimitUsageView(string Key, int Current, int Cap);

/// <summary>
/// Maps <c>GET /api/v1/edition</c> on the host. Anonymous (no
/// <c>[RequiresPermission]</c>, <c>[RequiresFeature]</c>, or
/// <c>[Authorize]</c>) — the dashboard's first-paint upsell requires a
/// read before sign-in. Wired from <c>HostComposer</c> next to the other
/// top-level endpoint registrations; lives under <c>Editions/</c> rather
/// than <c>Settings/</c> because edition state is governed by
/// <c>Comuki.Shared.Editions</c>, not by the platform options snapshot.
/// </summary>
public static class EditionsEndpoints
{
    /// <summary>Maps the edition endpoint under <see cref="ApiRoutes.Edition"/>.</summary>
    /// <param name="app">The endpoint route builder the host exposes.</param>
    /// <returns>The same builder, for chaining with sibling registrations.</returns>
    public static IEndpointRouteBuilder MapEditionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiRoutes.Edition).WithTags("Editions");
        group.MapGet("", GetAsync);
        return app;
    }

    private static async Task<EditionView> GetAsync(
        IEdition edition,
        IEditionCapabilityRegistry registry,
        IEnumerable<ILimitUsageProvider> limitProviders,
        CancellationToken cancellationToken)
    {
        var features = registry.Features
            .Select(feature => new FeatureAvailabilityView(feature.Key.Value, edition.Has(feature)))
            .ToArray();

        var providersByKey = limitProviders.ToDictionary(
            static provider => provider.LimitKey.Value,
            StringComparer.Ordinal);

        var limits = new List<LimitUsageView>(registry.Limits.Count);
        foreach (var limit in registry.Limits)
        {
            var current = 0;
            if (providersByKey.TryGetValue(limit.Key.Value, out var provider))
            {
                current = await provider.CurrentAsync(cancellationToken);
            }

            limits.Add(new LimitUsageView(limit.Key.Value, current, edition.Limit(limit)));
        }

        return new EditionView(
            Tier: edition.Current.Code,
            Status: edition.Status.Value,
            Features: features,
            Limits: limits,
            Version: ComukiBuildInfo.Read().Version,
            ExpiresAt: edition.ExpiresAt);
    }
}
