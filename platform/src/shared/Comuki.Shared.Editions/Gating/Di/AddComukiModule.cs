using System.Reflection;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comuki.Shared.Editions.Gating;

/// <summary>
/// <see cref="AddComukiModule{TModule}"/> — module-level edition gating at
/// the DI-registration boundary. Reads the module marker class for an
/// <see cref="EditionFeatureAttribute"/> and decides at composition time
/// whether to run the module's installer or skip it (the skipped module
/// is simply not registered; never a placeholder, never a throwing stub).
/// </summary>
public static class AddComukiModuleExtensions
{
    /// <summary>
    /// Module-level edition gating: runs <paramref name="installer"/> against
    /// <paramref name="services"/> only when the current
    /// <see cref="IEdition"/> covers the feature named by the
    /// <see cref="EditionFeatureAttribute"/> on <typeparamref name="TModule"/>.
    /// When the marker has no attribute, the module is ungated and the
    /// installer runs unconditionally (no bootstrap resolution at all —
    /// the common case must stay cheap).
    /// </summary>
    /// <typeparam name="TModule">
    /// The module marker type. Any class carrying (or not carrying) the
    /// attribute; a small private sealed marker class is the canonical
    /// shape so the attribute is the only reason the type exists.
    /// </typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="installer">The registration callback for the module's services.</param>
    /// <exception cref="InvalidOperationException">
    /// The marker carries an <see cref="EditionFeatureAttribute"/> but
    /// <see cref="IEdition"/> (or <see cref="IEditionCapabilityRegistry"/>)
    /// is not registered in <paramref name="services"/> at the time this
    /// method runs. Documented precondition: callers must wire the
    /// edition layer (e.g. via <c>AddComukiEditions(...)</c> plus a
    /// <see cref="IEditionCapabilityRegistry"/> registration) BEFORE
    /// calling this helper. We fail loudly at composition time rather
    /// than silently falling back to Community, which would hide a wiring
    /// gap behind a "module not registered" symptom in production.
    /// </exception>
    /// <remarks>
    /// <para>
    /// HOT-RELOAD SEMANTICS — this check runs EXACTLY ONCE, at the moment
    /// <c>AddComukiModule&lt;TModule&gt;()</c> executes during host
    /// composition (effectively boot time). A .NET DI container is
    /// immutable once <c>WebApplicationBuilder.Build()</c> runs, so there
    /// is no way to add a previously-skipped module's services after the
    /// fact.
    /// </para>
    /// <para>
    /// This is DIFFERENT from the request-time <see cref="IEdition.Has"/>
    /// reads the API gate (<see cref="RequiresFeatureFilter"/> /
    /// <see cref="RequiresFeatureMiddleware"/>) performs on every
    /// request — those hold a live <see cref="IEdition"/> and re-evaluate
    /// on every call because <c>IOptionsMonitor</c> reload is meaningful
    /// for a value read repeatedly at runtime. A DI <em>registration</em>
    /// decision has no such repeated read to hook a reload into.
    /// </para>
    /// <para>
    /// Consequence for operators: a license upgrade that newly unlocks a
    /// gated module's services requires a HOST RESTART to take effect.
    /// This is a real, intentional limitation, not an oversight.
    /// </para>
    /// <para>
    /// See <see cref="AddForEditionExtensions.AddForEdition{TService}"/>
    /// for the per-service counterpart that ALWAYS registers something
    /// (Community always gets a real, working <c>otherwise</c>
    /// implementation, never a null/throwing stub — issue #164's
    /// "one codebase, no data loss" open-core rule).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddComukiModule<TModule>(
        this IServiceCollection services,
        Action<IServiceCollection> installer)
    {
        var attribute = typeof(TModule).GetCustomAttribute<EditionFeatureAttribute>();
        if (attribute is null)
        {
            installer(services);
            return services;
        }

        using var bootstrap = services.BuildServiceProvider();
        var edition = bootstrap.GetRequiredService<IEdition>();
        var registry = bootstrap.GetRequiredService<IEditionCapabilityRegistry>();
        var logger = (bootstrap.GetService<ILoggerFactory>()
            ?? NullLoggerFactory.Instance).CreateLogger("Comuki.Shared.Editions.Gating");

        var featureKey = attribute.FeatureKey;
        FeatureKey? parsed = FeatureKey.IsWellFormed(featureKey)
            ? FeatureKey.Parse(featureKey)
            : null;
        var isCovered = parsed is { } parsedKey
            && registry.TryGetFeature(parsedKey, out var feature)
            && feature is not null
            && edition.Has(feature);

        if (!isCovered)
        {
            logger.LogWarning(
                "Module {Module} skipped: edition does not cover feature {FeatureKey}",
                typeof(TModule).Name,
                featureKey);
            return services;
        }

        installer(services);
        return services;
    }
}
