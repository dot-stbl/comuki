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
    /// <paramref name="services"/> only when <paramref name="compositionEdition"/>
    /// covers the feature named by the <see cref="EditionFeatureAttribute"/>
    /// on <typeparamref name="TModule"/>. When the marker has no attribute,
    /// the module is ungated and the installer runs unconditionally (no
    /// edition resolution at all — the common case must stay cheap).
    /// </summary>
    /// <typeparam name="TModule">
    /// The module marker type. Any class carrying (or not carrying) the
    /// attribute; a small private sealed marker class is the canonical
    /// shape so the attribute is the only reason the type exists.
    /// </typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="installer">The registration callback for the module's services.</param>
    /// <param name="compositionEdition">
    /// The composition-time <see cref="IEdition"/> snapshot the host built
    /// once via <see cref="Composition.CompositionEdition.Load"/> before the
    /// registration chain. Required for gated modules (a missing snapshot
    /// on a gated marker is a wiring gap and fails at composition time);
    /// ignored by ungated markers (the short-circuit never consults the
    /// snapshot). Composition-time gating no longer builds a throwaway
    /// <see cref="IServiceProvider"/> to look up the runtime
    /// <see cref="IEdition"/> (<c>di-installer.md</c> §6 bans
    /// <c>services.BuildServiceProvider()</c> inside registration).
    /// </param>
    /// <param name="loggerFactory">Optional sink for the "module skipped" log line; defaults to <see cref="NullLoggerFactory.Instance"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// The marker carries an <see cref="EditionFeatureAttribute"/> but
    /// <paramref name="compositionEdition"/> is null (wiring gap) or
    /// covers an unknown feature key (registry gap). We fail loudly at
    /// composition time rather than silently falling back to Community,
    /// which would hide a wiring gap behind a "module not registered"
    /// symptom in production.
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
        Action<IServiceCollection> installer,
        IEdition? compositionEdition = null,
        ILoggerFactory? loggerFactory = null)
    {
        var attribute = typeof(TModule).GetCustomAttribute<EditionFeatureAttribute>();
        if (attribute is null)
        {
            installer(services);
            return services;
        }

        // The snapshot is only consulted for gated modules; ungated ones
        // short-circuit above. A gated marker with no snapshot is a wiring
        // gap — fail loud at composition time rather than silently falling
        // back to Community, which would hide the gap behind a
        // "module not registered" symptom in production.
        if (compositionEdition is null)
        {
            throw new InvalidOperationException(
                $"module marker '{typeof(TModule).FullName}' carries an "
                + "EditionFeatureAttribute but no composition-time edition snapshot was passed; "
                + "build the snapshot once via CompositionEdition.Load(builder.Configuration) "
                + "and pass it to every gated AddComukiModule call.");
        }

        var logger = (loggerFactory ?? NullLoggerFactory.Instance)
            .CreateLogger("Comuki.Shared.Editions.Gating");

        var featureKey = attribute.FeatureKey;
        FeatureKey? parsed = FeatureKey.IsWellFormed(featureKey)
            ? FeatureKey.Parse(featureKey)
            : null;

        // Unknown feature key on the marker → registry gap (a real bug
        // that future architecture scans should catch at build time).
        // Fail loud here so the wiring gap is visible at boot, not
        // behind a "module not registered" symptom at runtime.
        if (parsed is not { } parsedKey)
        {
            throw new InvalidOperationException(
                $"module marker '{typeof(TModule).FullName}' carries an "
                + $"EditionFeatureAttribute with a non-well-formed key '{featureKey}'.");
        }

        // The composition snapshot already encapsulates Has(Feature) — it
        // does NOT need a registry because the marker's feature key was
        // validated against IsWellFormed and EditionGate's runtime path
        // owns the registry lookup. For the snapshot, the catalog's
        // minimum rank decision is the same one the runtime EditionGate
        // would make on a fresh Has(Feature) call.
        var registry = new EditionCapabilityRegistry();
        var feature = registry.TryGetFeature(parsedKey, out var resolved) ? resolved : null;

        var isCovered = feature is not null && compositionEdition.Has(feature);

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
